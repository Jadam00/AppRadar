# AppRadar

A .NET 10 CLI tool that generates short vertical marketing reels from local app images and metadata.
Produces H.264 MP4 videos built around a **single app**, following a 4-stage marketing narrative
with optional LLM-rewritten narration, Windows TTS audio, progressive caption sync, vertical scroll
slide transitions, and subtle drift animation — ready for Instagram Reels or TikTok-style uploads.

> **Windows Only** — AppRadar is designed and tested for **Windows 11 / Windows Server**.
> Linux and macOS are not supported.

---

## What's New

### Reel Renderer Refinements (v4)

- **Vertical scroll transitions** — slides move upward between caption chunks (`slideup` xfade); no
  more horizontal slide or random transition selection in structured mode.
- **Captions sourced from final narration text** — on-screen caption chunks are always split from the
  exact same narration paragraph used for TTS speech. There is no divergence between what is spoken
  and what is shown.
- **Shadow-free output by default** — `textShadow` is now `false` and `bottomGradientOpacity` is
  `0.0` by default. The output looks clean and flat. Both settings remain configurable.
- **Narration-length-driven timing** — each slide/chunk is displayed for a duration proportional to
  its share of the total audio. When chunk count matches slide count the mapping is exact; otherwise
  total duration is distributed equally. No fixed `secondsPerSlide` is used in narration-driven mode.

### Single-App Reel Engine (v3)

Each reel is a **one-app mini-advert**:

- **One app, one image** — a single app entry and a single screenshot are selected for the whole reel.
- **4-stage narrative flow** — Hook → Pain Point → Credibility → CTA — all about that one app.
- **Local LLM rewrite (optional)** — Ollama rewrites the four stage texts into one natural spoken paragraph.
- **Progressive caption sync** — on-screen text reveals in chunks that match the narration audio timing.
- **Narration-driven duration** — the reel lasts as long as the narration, not a fixed preset.

| Stage | Role | Purpose |
|---|---|---|
| 1 | **Hook** | Stop the scroll — bold claim, tension, or curiosity |
| 2 | **Pain Point** | Identify a problem or intriguing angle |
| 3 | **Credibility** | Evidence, contrast, build-up toward the reveal |
| 4 | **CTA** | Your app as the payoff with a clear call-to-action |

All four stages are preserved as structured data in the manifest, regardless of whether LLM rewriting is enabled.

---

## Prerequisites

| Requirement | Details |
|---|---|
| **Windows 11 / Windows Server** | Required platform |
| **.NET 10 SDK** | https://dotnet.microsoft.com/download/dotnet/10.0 |
| **FFmpeg** | Used for video encoding and audio muxing — see [FFmpeg Setup](#ffmpeg-setup) |
| **Ollama** *(optional)* | Local LLM server for narration rewriting — see [Ollama Setup](#ollama-setup-optional) |

Verify your .NET SDK version in PowerShell:

```powershell
dotnet --version   # must be 10.x
```

---

## Quick Start (PowerShell)

```powershell
# 1. Clone and open repo
git clone <repo-url>
cd AppRadar

# 2. Run the setup script to validate all prerequisites
.\Setup.ps1

# 3. Generate placeholder images for testing (no real screenshots needed)
dotnet run --project src\AppRadar -- setup --input input

# 4. Generate a single-app reel (no audio, no LLM)
dotnet run --project src\AppRadar -- generate --count 1 --duration 16

# 5. Generate a reel with TTS narration audio (Windows SAPI)
dotnet run --project src\AppRadar -- generate --count 1 --with-audio true

# 6. Generate a reel with LLM narration rewrite + audio
#    (requires Ollama running locally with qwen3:8b or your chosen model)
dotnet run --project src\AppRadar -- generate --count 1 --with-audio true
#    and set "llm": { "enabled": true } in input\config.json
```

---

## FFmpeg Setup

AppRadar uses FFmpeg for video encoding and audio muxing. Install it using one of the following methods:

### Option 1 — winget (recommended)

```powershell
winget install Gyan.FFmpeg
```

Restart your terminal after installation, then verify:

```powershell
ffmpeg -version
```

### Option 2 — Chocolatey

```powershell
choco install ffmpeg
```

### Option 3 — Scoop

```powershell
scoop install ffmpeg
```

### Option 4 — Manual install

1. Download a Windows build from https://ffmpeg.org/download.html#build-windows
2. Extract to `C:\ffmpeg`
3. Add `C:\ffmpeg\bin` to your `PATH` environment variable, **or** set the explicit path in `input\config.json`:

```json
{
  "tools": {
    "ffmpegPath": "C:\\ffmpeg\\bin\\ffmpeg.exe"
  }
}
```

### Option 5 — Environment variable

```powershell
$env:FFMPEG_PATH = "C:\ffmpeg\bin\ffmpeg.exe"
```

### How FFmpeg is discovered

AppRadar searches in this order and uses the first match:

1. `tools.ffmpegPath` value in `input\config.json`
2. `FFMPEG_PATH` environment variable
3. Common Windows install locations:
   - `C:\ffmpeg\bin\ffmpeg.exe`
   - `C:\Program Files\ffmpeg\bin\ffmpeg.exe`
   - Chocolatey: `%ProgramData%\chocolatey\bin\ffmpeg.exe`
   - Scoop: `%USERPROFILE%\scoop\shims\ffmpeg.exe`
   - WinGet: `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg_*\...\bin\ffmpeg.exe`
4. Each directory listed in the `PATH` environment variable (checks `ffmpeg.exe` and `ffmpeg`)
5. Direct execution probe (covers shims and wrappers not represented as plain files)

---

## Ollama Setup (optional)

AppRadar can optionally use a locally running [Ollama](https://ollama.com) server to rewrite the
four raw marketing stage texts into one short, natural-sounding narration paragraph.

This step is **entirely optional**. When Ollama is disabled or unavailable, AppRadar falls back
to a deterministic sentence join (see [Fallback Behaviour](#fallback-behaviour)).

### 1. Install Ollama

Download and install Ollama from https://ollama.com/download (Windows installer available).

### 2. Pull a model

The default recommended model is `qwen3:8b`:

```powershell
ollama pull qwen3:8b
```

Any text-capable Ollama model works. To use a different model, set `llm.ollama.model` in config.

### 3. Start Ollama

Ollama typically runs automatically after installation. Verify it is listening:

```powershell
curl http://localhost:11434/api/tags
```

### 4. Enable LLM rewriting in AppRadar

Edit `input\config.json`:

```json
{
  "llm": {
    "enabled": true,
    "provider": "Ollama",
    "ollama": {
      "baseUrl": "http://localhost:11434",
      "model": "qwen3:8b",
      "timeoutSeconds": 30,
      "temperature": 0.4
    },
    "fallbackToDeterministicJoin": true
  }
}
```

### Changing the model

Set `llm.ollama.model` to any model you have pulled locally:

```json
"ollama": { "model": "llama3.2" }
```

### Fallback Behaviour

When `llm.enabled` is `false`, Ollama is unreachable, or the request fails:

1. AppRadar logs a warning.
2. A deterministic join is used instead: `"{Hook}. {Pain}. {Credibility}. {CTA}."`
3. Reel generation continues normally.
4. The manifest records `"llmFallbackUsed": true`.

The fallback always produces valid narration, so Ollama failure never blocks reel generation.

---

## Piper TTS Setup

[Piper](https://github.com/rhasspy/piper) is a fast, local neural TTS engine that runs entirely
on your machine — no cloud API, no internet required.  AppRadar can use it instead of the
built-in Windows SAPI synthesizer.

### 1. Download Piper

Download the latest Windows release from:

> https://github.com/rhasspy/piper/releases

Extract the archive to a permanent location, for example `C:\piper`.  
You should have at minimum:

```
C:\piper\
  piper.exe
  espeak-ng-data\   (required by Piper)
```

### 2. Download a voice model

Browse available voice models at:

> https://huggingface.co/rhasspy/piper-voices

Each voice requires **two files**:
- `<voice>.onnx` — the model weights
- `<voice>.onnx.json` — the model config (must be in the same folder)

Example — download the `en_GB-alan-medium` voice:

```powershell
New-Item -ItemType Directory -Path C:\piper\models -Force

# Model weights
Invoke-WebRequest `
  -Uri "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/alan/medium/en_GB-alan-medium.onnx" `
  -OutFile "C:\piper\models\en_GB-alan-medium.onnx"

# Model config (must sit next to the .onnx file)
Invoke-WebRequest `
  -Uri "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/alan/medium/en_GB-alan-medium.onnx.json" `
  -OutFile "C:\piper\models\en_GB-alan-medium.onnx.json"
```

### 3. Configure AppRadar

Edit `input\config.json`:

```json
{
  "audio": {
    "enabled": true,
    "ttsProvider": "Piper",
    "leadInMs": 150,
    "normalizeAudio": true,
    "piper": {
      "exePath": "C:\\piper\\piper.exe",
      "modelPath": "C:\\piper\\models\\en_GB-alan-medium.onnx",
      "speaker": null,
      "lengthScale": 1.0,
      "noiseScale": 0.667,
      "noiseW": 0.8
    }
  }
}
```

| Field | Default | Description |
|---|---|---|
| `exePath` | *(required)* | Full path to `piper.exe` |
| `modelPath` | *(required)* | Full path to the `.onnx` voice model |
| `speaker` | `null` | Speaker ID for multi-speaker models; `null` for single-speaker voices |
| `lengthScale` | `1.0` | Speech speed — values below `1.0` speed up, above `1.0` slow down |
| `noiseScale` | `0.667` | Voice variation / expressiveness |
| `noiseW` | `0.8` | Phoneme duration variation |

### 4. Generate a reel with Piper narration

```powershell
dotnet run --project src\AppRadar -- generate --count 1 --with-audio true
```

### 5. Example Piper command (manual test)

Verify your Piper installation independently before using it with AppRadar:

```powershell
& "C:\piper\piper.exe" `
  --model "C:\piper\models\en_GB-alan-medium.onnx" `
  --output_file "C:\temp\test.wav" `
  --text "Hello world, this is a test"
```

### Troubleshooting Piper

| Error | Likely cause | Fix |
|---|---|---|
| `Piper executable not found` | Wrong `exePath` | Check the path in config.json; include `piper.exe` at the end |
| `Piper voice model not found` | Wrong `modelPath` | Verify both `.onnx` and `.onnx.json` exist in the same folder |
| `Piper TTS failed (exit code 1)` | Model/executable mismatch | Ensure `piper.exe` version matches the downloaded model |
| `Piper process timed out` | Very long text segment | Shorten the narration, or increase system resources |
| No audio in output MP4 | `audio.enabled` is `false` | Set `"enabled": true` in config, or pass `--with-audio true` |

---

## Setup Script

Run `Setup.ps1` from the repository root to validate your environment:

```powershell
.\Setup.ps1
```

It checks:
- Windows platform
- .NET 10 SDK presence
- FFmpeg presence and version
- Required input/output folders (creates them if missing)
- Input metadata files
- Project build

To also generate placeholder images during setup:

```powershell
.\Setup.ps1 -GeneratePlaceholders
```

---

## Configuration

`input\config.json` — all sections are optional (shown with defaults):

```json
{
  "video": {
    "width": 1080,
    "height": 1920,
    "fps": 30,
    "defaultDurationSeconds": 24,
    "secondsPerSlide": 4
  },
  "overlay": {
    "fontFamily": "Arial",
    "fontSize": 72,
    "fontColor": "#FFFFFF",
    "padding": 64,
    "maxTextWidthPercent": 0.82,
    "bottomGradientOpacity": 0.0,
    "textShadow": false
  },
  "animation": {
    "verticalDriftPixels": 60,
    "transitionDurationMs": 600
  },
  "tools": {
    "ffmpegPath": null,
    "ffprobePath": null
  },
  "audio": {
    "enabled": false,
    "ttsProvider": "SystemSpeech",
    "voiceName": null,
    "rate": 0,
    "volume": 100,
    "leadInMs": 150,
    "gapBetweenSlidesMs": 300,
    "normalizeAudio": true,
    "piper": {
      "exePath": null,
      "modelPath": null,
      "speaker": null,
      "lengthScale": 1.0,
      "noiseScale": 0.667,
      "noiseW": 0.8
    }
  },
  "strategy": {
    "mode": "StructuredMarketing",
    "allowLegacyShuffleMode": true
  },
  "llm": {
    "enabled": false,
    "provider": "Ollama",
    "ollama": {
      "baseUrl": "http://localhost:11434",
      "model": "qwen3:8b",
      "timeoutSeconds": 30,
      "temperature": 0.4
    },
    "fallbackToDeterministicJoin": true
  },
  "captionSync": {
    "mode": "Chunked",
    "tailHoldMs": 800,
    "minVisualDurationMs": 4000
  }
}
```

### Overlay / visual configuration

| Field | Default | Description |
|---|---|---|
| `overlay.fontFamily` | `"Arial"` | Font family for captions and app name |
| `overlay.fontSize` | `72` | Font size in points |
| `overlay.fontColor` | `"#FFFFFF"` | Caption text colour (hex) |
| `overlay.padding` | `64` | Pixel padding from canvas edges |
| `overlay.maxTextWidthPercent` | `0.82` | Maximum caption width as a fraction of canvas width |
| `overlay.bottomGradientOpacity` | `0.0` | Opacity of the bottom gradient overlay (0 = none, 1 = fully black). Default `0.0` — shadow-free. |
| `overlay.textShadow` | `false` | Draw a drop shadow under caption text. Default `false` — shadow-free. |

> **Shadow-free by default.** Both `textShadow` and `bottomGradientOpacity` are off by default.
> To re-enable a subtle gradient for readability on bright images set `"bottomGradientOpacity": 0.35`.
> To re-enable text shadow set `"textShadow": true`.

### Audio configuration

| Field | Default | Description |
|---|---|---|
| `enabled` | `false` | Generate and mux narration audio into the MP4 |
| `ttsProvider` | `"SystemSpeech"` | TTS backend: `"SystemSpeech"` (Windows SAPI) or `"Piper"` (Piper neural TTS) |
| `voiceName` | `null` | SAPI voice name (e.g. `"Microsoft Zira Desktop"`). `null` = system default. Only used by `SystemSpeech`. |
| `rate` | `0` | Speaking rate: `-10` (slowest) to `10` (fastest). Only used by `SystemSpeech`. |
| `volume` | `100` | Volume 0–100. Only used by `SystemSpeech`. |
| `leadInMs` | `150` | Silence before first word of narration (ms) |
| `gapBetweenSlidesMs` | `300` | Silence between narration segments in legacy mode (ms) |
| `normalizeAudio` | `true` | Apply FFmpeg `loudnorm` filter to even out audio levels |

### LLM (Ollama) configuration

| Field | Default | Description |
|---|---|---|
| `llm.enabled` | `false` | Enable LLM rewrite of the 4-stage draft into one narration paragraph |
| `llm.provider` | `"Ollama"` | LLM provider — only `"Ollama"` is currently supported |
| `llm.ollama.baseUrl` | `"http://localhost:11434"` | Base URL of the local Ollama server |
| `llm.ollama.model` | `"qwen3:8b"` | Model to use for narration rewriting |
| `llm.ollama.timeoutSeconds` | `30` | HTTP request timeout in seconds |
| `llm.ollama.temperature` | `0.4` | LLM sampling temperature (0.0–1.0, lower = more deterministic) |
| `llm.fallbackToDeterministicJoin` | `true` | Fall back gracefully when Ollama is unavailable |

### Caption sync configuration

| Field | Default | Description |
|---|---|---|
| `captionSync.mode` | `"Chunked"` | How narration is split for progressive reveal (`"Chunked"` splits at sentence boundaries) |
| `captionSync.tailHoldMs` | `800` | Extra hold time after the last narration word ends (ms) |
| `captionSync.minVisualDurationMs` | `4000` | Minimum total visual duration when narration is very short (ms) |

### Slide transition and timing

In `StructuredMarketing` mode, the pipeline always uses a **vertical scroll** (`slideup`) transition
between caption chunks so the viewer perceives a smooth upward movement.

**Narration-length-driven timing** — when audio is generated, each slide/chunk is displayed for a
duration proportional to its word count within the total narration length.  The total video length
equals `measuredAudioDurationMs + tailHoldMs` (clamped to `minVisualDurationMs`).  No fixed
`secondsPerSlide` value is applied in narration-driven mode.

If audio is disabled, the pipeline falls back to fixed `secondsPerSlide` with cycling to fill
`defaultDurationSeconds`.

### Strategy configuration

| Field | Default | Description |
|---|---|---|
| `mode` | `"StructuredMarketing"` | `"StructuredMarketing"` (single-app 4-stage) or `"Legacy"` (original random shuffle) |
| `allowLegacyShuffleMode` | `true` | When `true`, `--strategy legacy` is accepted on the CLI |

### Supported environment variables

| Variable | Purpose |
|---|---|
| `FFMPEG_PATH` | Full path to `ffmpeg.exe` — overrides automatic discovery |
| `FFPROBE_PATH` | Full path to `ffprobe.exe` — not currently used in the pipeline |

---

## Folder Structure

```
AppRadar\
├── src\
│   └── AppRadar\                   # Main CLI application
│       ├── Audio\                  # ITtsProvider, WavDurationReader, TtsProviderFactory
│       ├── Commands\
│       ├── Config\
│       ├── Llm\                    # ICaptionRewriteProvider, OllamaProvider, DeterministicJoiner
│       ├── Models\                 # ReelStoryDraft, ReelNarrationPlan, DisplayChunk, …
│       ├── Rendering\
│       ├── Services\               # ReelPlanner, NarrationPlanner, GenerationService, …
│       ├── Utilities\
│       ├── Video\
│       └── Program.cs
├── tests\
│   └── AppRadar.Tests\             # Unit and integration tests
├── input\
│   ├── config.json                 # Optional configuration overrides
│   ├── featuredApps\
│   │   ├── images\                 # Featured app screenshots (.png/.jpg)
│   │   └── description.json        # App metadata with structured caption fields
│   └── myApps\
│       ├── images\                 # Your app screenshots
│       └── description.json        # Your app metadata with all 4-stage captions
├── output\
│   ├── audio\                      # Generated narration WAV files
│   ├── images\                     # Rendered slide PNGs
│   ├── videos\                     # Generated MP4 files
│   └── manifests\                  # JSON manifest per reel
├── temp\                           # Temporary processing files
├── Setup.ps1                       # Windows setup and validation script
└── AppRadar.slnx                   # Solution file
```

---

## Metadata Format

`myApps\description.json` — the app being promoted. All four role-specific caption pools should
be populated for best results. The planner picks ONE entry (one image) and draws captions from
all four pools for the single-app reel.

`featuredApps\description.json` — optional reference apps. Not used in `StructuredMarketing` mode
(the reel is built from `myApps`), but kept for `Legacy` mode compatibility.

### Recommended myApps schema

```json
{
  "apps": [
    {
      "imageName": "my_app_screenshot.png",
      "appName": "My App Name",
      "captions": [
        "Generic fallback caption"
      ],
      "hookCaptions": [
        "Hard hook line — stop the scroll",
        "Bold opening claim"
      ],
      "painPointCaptions": [
        "The problem this app solves",
        "Why existing solutions fall short"
      ],
      "credibilityCaptions": [
        "Why this app is genuinely different",
        "Evidence or contrast"
      ],
      "ctaCaptions": [
        "Download My App and see for yourself",
        "Try it today"
      ],
      "tags": ["productivity", "utility"],
      "enabled": true
    }
  ]
}
```

**Role-specific caption fields are optional.**  
When a role-specific list is absent or empty, the planner falls back to the generic
`captions` list, then to built-in templates derived from the app's tags and name.

**Validation rules:**
- `enabled: false` items are skipped entirely
- `imageName` must be unique within the file
- The image file referenced by `imageName` must exist in the `images\` subfolder
- `captions` must not be empty (at least one generic caption is required as fallback)
- Role-specific caption fields (`hookCaptions`, `painPointCaptions`, etc.) are optional
- At least **1 enabled** myApp is required

---

## Getting Started

### 1. Clone and build

```powershell
git clone <repo-url>
cd AppRadar
dotnet build
```

### 2. Generate placeholder images (optional)

If you don't have real app screenshots yet, generate colorful placeholder images for testing:

```powershell
dotnet run --project src\AppRadar -- setup --input input
```

### 3. Generate a single-app marketing reel

```powershell
dotnet run --project src\AppRadar -- generate --count 1 --duration 16
```

### 4. Generate a reel with narration audio (Windows)

```powershell
dotnet run --project src\AppRadar -- generate --count 1 --with-audio true
```

Output files are written to `output\videos\`, `output\images\`, `output\audio\`, and `output\manifests\`.

---

## Commands

### `generate`

Generates one or more reel videos.

```
appradar generate [options]

Options:
  --count <number>         Number of reels to generate (default: 1)
  --duration <seconds>     Minimum reel duration in seconds; overridden by narration length (default: 24)
  --seed <number>          Seed for deterministic output (optional)
  --fps <number>           Frames per second, overrides config (optional)
  --input <path>           Input directory path (default: input)
  --output <path>          Output directory path (default: output)
  --with-audio <bool>      Generate narration audio: true or false (overrides config audio.enabled)
  --strategy <name>        Narrative strategy: structured (default) or legacy
```

### `setup`

Generates placeholder PNG images from your `description.json` files.

```
appradar setup [--input <path>]
```

---

## Example Commands (PowerShell)

```powershell
# Generate 1 single-app reel, no audio
dotnet run --project src\AppRadar -- generate --count 1 --duration 16

# Generate a reel with Windows SAPI narration audio
dotnet run --project src\AppRadar -- generate --count 1 --with-audio true

# Generate a reel with Ollama LLM rewrite + Piper audio
# (requires llm.enabled=true and audio.ttsProvider=Piper in config)
dotnet run --project src\AppRadar -- generate --count 1 --with-audio true

# Use the original legacy shuffle mode
dotnet run --project src\AppRadar -- generate --count 1 --duration 24 --strategy legacy

# Deterministic output with seed
dotnet run --project src\AppRadar -- generate --count 2 --duration 16 --seed 123

# Generate 5 reels (each uses a different image of the same app)
dotnet run --project src\AppRadar -- generate --count 5 --duration 16

# Custom input/output directories
dotnet run --project src\AppRadar -- generate --count 1 --input .\input --output .\output

# Generate placeholder images for testing
dotnet run --project src\AppRadar -- setup --input input

# Use explicit FFmpeg path via environment variable
$env:FFMPEG_PATH = "C:\ffmpeg\bin\ffmpeg.exe"
dotnet run --project src\AppRadar -- generate --count 1 --duration 16
```

---

## How the Narrative Engine Works

### StructuredMarketing mode (default)

Each reel promotes **one app** using a deliberate 4-stage narrative:

1. **SELECT** — Pick exactly 1 app entry from `myApps` (one image). Multiple entries for the same app with different screenshots are all eligible; one is selected per reel.
2. **DRAFT** — Build a `ReelStoryDraft` with 4 stage texts from that app's caption pools (hook → pain → cred → CTA). Stage texts come from role-specific caption lists first, then generic captions, then built-in templates.
3. **REWRITE** *(optional, requires Ollama)* — A local LLM rewrites the 4 stage texts into one short spoken paragraph.
4. **SPLIT** — The narration paragraph is split into display chunks at sentence/phrase boundaries.
5. **RENDER** — 4 slide images are generated from the **same base image**, each showing one caption chunk.
6. **TTS** *(optional)* — The full narration paragraph is synthesised to a WAV file.
7. **MEASURE** — The actual WAV duration is read from the file header.
8. **TIMELINE** — Chunk display timings are derived from the real audio duration (proportional to word count).
9. **COMPOSE** — FFmpeg composes the final MP4. Duration = audio duration + tail hold (not a fixed preset).

### How narration length drives reel duration

- The reel stays on screen for as long as the narration lasts.
- After TTS generation, the actual WAV duration is measured (no estimates).
- Final video duration = max(audio duration + `captionSync.tailHoldMs`, `captionSync.minVisualDurationMs`).
- `--duration` sets a fallback minimum, not a hard cap.

### Progressive caption sync

- The narration paragraph is split at sentence/punctuation boundaries into display chunks.
- Each chunk is displayed for a time window proportional to its word count relative to total words.
- No chunk extends beyond (audio duration + tail hold).
- The on-screen text and the narration audio come from **exactly the same text source**.

### Legacy mode

Passing `--strategy legacy` restores the original behaviour:
- 3 random featured apps selected for the first 3 slides
- 1 random myApp selected for the CTA slide
- Order shuffled randomly

### Determinism

When `--seed` is provided:
- All random selections use a seeded `System.Random` instance
- For multiple reels (`--count > 1`), reel N uses `seed + (N - 1)`
- Same seed always produces identical output

---

## How TTS Narration Works

When audio is enabled in `StructuredMarketing` mode:

1. **Single-paragraph TTS** — The final narration paragraph (LLM-rewritten or deterministic fallback) is passed to the TTS provider as a single segment.
2. **Duration measurement** — The actual WAV duration is read from the output file header.
3. **Video duration** — The video is rendered to match the measured audio duration plus `captionSync.tailHoldMs`.
4. **Mux** — The video and audio are combined into the final MP4.

Two TTS providers are available:
- **SystemSpeech** (default) — uses the built-in Windows SAPI `SpeechSynthesizer` (`System.Speech`). No setup required on Windows.
- **Piper** — uses the [Piper](https://github.com/rhasspy/piper) neural TTS engine (external executable). Produces higher-quality, more natural-sounding speech. Requires a separate download — see [Piper TTS Setup](#piper-tts-setup).

**Selecting a provider:**
```json
"audio": { "ttsProvider": "Piper" }
```

**SystemSpeech voice selection:**  
Set `audio.voiceName` to use a specific SAPI voice:

```json
"audio": { "voiceName": "Microsoft Zira Desktop" }
```

Run `dotnet run -- setup --list-voices` to list available Windows SAPI voices.

---

## Manifest Format

Each generated reel produces a JSON manifest in `output\manifests\`. Key fields:

| Field | Description |
|---|---|
| `generationId` | Unique ID for this reel |
| `seedUsed` | RNG seed used |
| `selectedAppName` | The single app promoted in this reel |
| `selectedImageName` | The single image used throughout the reel |
| `hookText` | Stage 1 raw text (pre-LLM) |
| `painPointText` | Stage 2 raw text (pre-LLM) |
| `credibilityText` | Stage 3 raw text (pre-LLM) |
| `ctaText` | Stage 4 raw text (pre-LLM) |
| `narrationText` | Final narration paragraph (LLM-rewritten or deterministic fallback) |
| `llmModelUsed` | Ollama model used, or `null` if fallback was used |
| `ttsProvider` | TTS provider used for audio generation |
| `audioDurationMs` | Measured audio duration in ms |
| `finalVideoDurationMs` | Final video duration in ms |
| `llmFallbackUsed` | `true` if Ollama was unavailable and deterministic join was used |
| `revealTimeline` | Array of `{ text, startMs, durationMs }` for progressive caption sync |

---

## Troubleshooting

| Problem | Likely cause | Fix |
|---|---|---|
| `No app sources available` | `myApps\description.json` is empty or all entries disabled | Enable at least one myApp entry |
| `At least 1 enabled myApp is required` | All myApp entries have `"enabled": false` or no valid images | Add or enable a myApp entry with a valid image |
| No audio in output MP4 | `audio.enabled` is `false` | Set `"enabled": true` or pass `--with-audio true` |
| LLM rewrite skipped | `llm.enabled` is `false` (default) | Set `"llm": { "enabled": true }` and ensure Ollama is running |
| Ollama fallback used | Ollama not running or model not pulled | Start Ollama and run `ollama pull qwen3:8b` |
| Very short reel duration | Narration is short, minVisualDurationMs applies | Increase `captionSync.minVisualDurationMs` or add more content to captions |

