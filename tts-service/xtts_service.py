#!/usr/bin/env python3
import argparse
import json
import os
import tempfile
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import numpy as np
import soundfile as sf
import torch
import torchaudio

# Coqui TTS 0.22.0 loads pickled checkpoint objects. Torch 2.6+ defaults
# torch.load(weights_only=True), which breaks this model load path.
os.environ.setdefault("TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD", "1")

from TTS.api import TTS

MODEL_NAME = "tts_models/multilingual/multi-dataset/xtts_v2"
DEFAULT_LANGUAGE = "en"

_MODEL: Optional[TTS] = None
_DEVICE = "cpu"


def _torchaudio_load_with_soundfile(
    uri: str,
    frame_offset: int = 0,
    num_frames: int = -1,
    normalize: bool = True,
    channels_first: bool = True,
    format: Optional[str] = None,
    buffer_size: int = 4096,
    backend: Optional[str] = None,
):
    """Fallback for torchaudio.load that avoids TorchCodec/FFmpeg runtime coupling.

    XTTS speaker references in this project are WAV files, so soundfile-based
    decoding is sufficient and more stable across torch nightly combinations.
    """
    del normalize, format, buffer_size, backend

    audio, sample_rate = sf.read(uri, always_2d=True, dtype="float32")

    start = max(0, int(frame_offset))
    if int(num_frames) >= 0:
        end = start + int(num_frames)
        audio = audio[start:end, :]
    else:
        audio = audio[start:, :]

    if channels_first:
        tensor = torch.from_numpy(np.ascontiguousarray(audio.T))
    else:
        tensor = torch.from_numpy(np.ascontiguousarray(audio))

    return tensor, int(sample_rate)


torchaudio.load = _torchaudio_load_with_soundfile


def load_model_once() -> Tuple[TTS, str]:
    global _MODEL
    global _DEVICE

    if _MODEL is not None:
        return _MODEL, _DEVICE

    device = "cuda" if torch.cuda.is_available() else "cpu"
    tts = TTS(model_name=MODEL_NAME)
    tts.to(device)

    _MODEL = tts
    _DEVICE = device
    return tts, device


def to_mono(audio: np.ndarray) -> np.ndarray:
    if audio.ndim == 1:
        return audio.astype(np.float32)
    return np.mean(audio, axis=1, dtype=np.float32)


def resample_linear(audio: np.ndarray, src_rate: int, target_rate: int) -> np.ndarray:
    if src_rate == target_rate:
        return audio.astype(np.float32)

    if len(audio) == 0:
        return audio.astype(np.float32)

    duration = len(audio) / float(src_rate)
    target_len = max(1, int(round(duration * target_rate)))

    src_x = np.linspace(0.0, 1.0, num=len(audio), endpoint=False)
    dst_x = np.linspace(0.0, 1.0, num=target_len, endpoint=False)
    return np.interp(dst_x, src_x, audio).astype(np.float32)


def collect_voice_wavs(voice_path: str) -> List[Path]:
    base = Path(voice_path)
    if not base.exists() or not base.is_dir():
        raise FileNotFoundError(f"voicePath folder not found: {voice_path}")

    wavs = sorted([p for p in base.glob("*.wav") if p.is_file()])
    if not wavs:
        raise FileNotFoundError(f"No .wav reference files found in: {voice_path}")

    return wavs


def merge_reference_wavs(wavs: List[Path]) -> Tuple[str, Optional[str]]:
    if len(wavs) == 1:
        return str(wavs[0]), None

    chunks: List[np.ndarray] = []
    target_rate = 24000
    silence = np.zeros(int(target_rate * 0.15), dtype=np.float32)

    for wav in wavs:
        audio, sample_rate = sf.read(str(wav), always_2d=False)
        mono = to_mono(np.asarray(audio))
        mono = resample_linear(mono, int(sample_rate), target_rate)
        chunks.append(mono)
        chunks.append(silence)

    merged = np.concatenate(chunks).astype(np.float32)

    temp_file = tempfile.NamedTemporaryFile(prefix="xtts_ref_", suffix=".wav", delete=False)
    temp_file.close()
    sf.write(temp_file.name, merged, target_rate, subtype="PCM_16")
    return temp_file.name, temp_file.name


def inspect_duration_seconds(file_path: str) -> float:
    info = sf.info(file_path)
    if info.samplerate <= 0:
        return 0.0
    return float(info.frames) / float(info.samplerate)


def synthesize(text: str, voice_path: str, output_path: str) -> Dict[str, Any]:
    if not text or not text.strip():
        raise ValueError("text must not be empty")

    tts, _ = load_model_once()
    refs = collect_voice_wavs(voice_path)
    speaker_wav, temp_ref = merge_reference_wavs(refs)

    out_path = Path(output_path)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    try:
        tts.tts_to_file(
            text=text.strip(),
            speaker_wav=speaker_wav,
            language=DEFAULT_LANGUAGE,
            file_path=str(out_path),
        )
    finally:
        if temp_ref and Path(temp_ref).exists():
            Path(temp_ref).unlink(missing_ok=True)

    if not out_path.exists():
        raise FileNotFoundError(f"XTTS did not generate output file: {output_path}")

    duration = inspect_duration_seconds(str(out_path))
    return {
        "success": True,
        "durationSeconds": round(duration, 3),
        "filePath": str(out_path),
    }


class XttsHandler(BaseHTTPRequestHandler):
    server_version = "XTTSService/1.0"

    def _send_json(self, status: int, payload: Dict[str, Any]) -> None:
        encoded = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def log_message(self, format: str, *args: Any) -> None:
        return

    def do_GET(self) -> None:
        if self.path == "/health":
            _, device = load_model_once()
            self._send_json(200, {"success": True, "model": MODEL_NAME, "device": device})
            return

        self._send_json(404, {"success": False, "error": "Not found"})

    def do_POST(self) -> None:
        if self.path != "/tts":
            self._send_json(404, {"success": False, "error": "Not found"})
            return

        try:
            content_length = int(self.headers.get("Content-Length", "0"))
            raw = self.rfile.read(content_length)
            payload = json.loads(raw.decode("utf-8"))

            text = str(payload.get("text", ""))
            voice_path = str(payload.get("voicePath", ""))
            output_path = str(payload.get("outputPath", ""))

            if not output_path:
                raise ValueError("outputPath must not be empty")

            result = synthesize(text, voice_path, output_path)
            self._send_json(200, result)
        except Exception as ex:
            self._send_json(
                500,
                {
                    "success": False,
                    "error": str(ex),
                    "trace": traceback.format_exc(limit=2),
                },
            )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="XTTS v2 local HTTP service")
    parser.add_argument("--host", default="localhost")
    parser.add_argument("--port", type=int, default=8020)
    return parser.parse_args()


def main() -> None:
    args = parse_args()

    # Warm model once so first request latency is reduced.
    _, device = load_model_once()
    print(f"XTTS model loaded: {MODEL_NAME} on {device}")

    server = ThreadingHTTPServer((args.host, args.port), XttsHandler)
    print(f"XTTS service listening on http://{args.host}:{args.port}")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
