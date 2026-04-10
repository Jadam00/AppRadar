namespace AppRadar.Config;

/// <summary>
/// Paths to external tools required by AppRadar.
/// All settings are optional; AppRadar will search PATH and common Windows install locations
/// if no explicit value is provided.
/// </summary>
public sealed class ToolsConfig
{
    /// <summary>
    /// Full path to ffmpeg.exe.
    /// Example: C:\ffmpeg\bin\ffmpeg.exe
    /// Overrides automatic discovery when set.
    /// Can also be set via the FFMPEG_PATH environment variable.
    /// </summary>
    public string? FFmpegPath { get; set; }

    /// <summary>
    /// Full path to ffprobe.exe (optional — not currently used in the pipeline).
    /// Example: C:\ffmpeg\bin\ffprobe.exe
    /// Can also be set via the FFPROBE_PATH environment variable.
    /// </summary>
    public string? FFprobePath { get; set; }
}

public sealed class VideoConfig
{
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 1920;
    public int Fps { get; set; } = 30;
    public int DefaultDurationSeconds { get; set; } = 24;
    public int SecondsPerSlide { get; set; } = 3;
}

public sealed class OverlayConfig
{
    public string FontFamily { get; set; } = "Arial";
    public float FontSize { get; set; } = 72f;
    public string FontColor { get; set; } = "#FFFFFF";
    public int Padding { get; set; } = 64;
    public float MaxTextWidthPercent { get; set; } = 0.82f;
    /// <summary>
    /// Opacity of the top/bottom gradient overlays (0.0 = fully transparent, 1.0 = fully opaque).
    /// Defaults to 0.0 (no gradient) for a clean, shadow-free look.  Increase if text contrast
    /// requires it on your images.
    /// </summary>
    public float BottomGradientOpacity { get; set; } = 0.0f;

    /// <summary>
    /// Whether to draw a drop-shadow behind caption and app-name text.
    /// Defaults to false for a clean, shadow-free look.
    /// Set to true in config.json to re-enable.
    /// </summary>
    public bool TextShadow { get; set; } = false;

    /// <summary>
    /// Multiplier applied to <see cref="Padding"/> to lift the caption upward from the
    /// bottom edge of the fitted image area.
    /// Higher values place captions higher on the image.
    /// Default 0.7.
    /// </summary>
    public float CaptionLiftFactor { get; set; } = 0.7f;

    /// <summary>
    /// Enables a rounded background pill behind captions for improved readability.
    /// </summary>
    public bool CaptionPillEnabled { get; set; } = true;

    /// <summary>
    /// Pill background color in hex format.
    /// </summary>
    public string CaptionPillColor { get; set; } = "#000000";

    /// <summary>
    /// Pill background opacity (0.0 to 1.0).
    /// </summary>
    public float CaptionPillOpacity { get; set; } = 0.56f;

    /// <summary>
    /// Horizontal padding around caption text inside the pill.
    /// </summary>
    public int CaptionPillHorizontalPadding { get; set; } = 30;

    /// <summary>
    /// Vertical padding around caption text inside the pill.
    /// </summary>
    public int CaptionPillVerticalPadding { get; set; } = 20;

    /// <summary>
    /// Rounded corner radius for caption pill background.
    /// </summary>
    public float CaptionPillCornerRadius { get; set; } = 44f;

    /// <summary>
    /// Draws a subtle stroke around the pill when enabled.
    /// </summary>
    public bool CaptionPillStrokeEnabled { get; set; } = true;

    /// <summary>
    /// Stroke color for caption pill in hex format.
    /// </summary>
    public string CaptionPillStrokeColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// Stroke opacity (0.0 to 1.0) for caption pill border.
    /// </summary>
    public float CaptionPillStrokeOpacity { get; set; } = 0.16f;

    /// <summary>
    /// Stroke thickness in pixels for caption pill border.
    /// </summary>
    public float CaptionPillStrokeWidth { get; set; } = 2f;
}

public sealed class AnimationConfig
{
    public int TransitionDurationMs { get; set; } = 600;

    /// <summary>
    /// Deterministic transition sequence used across slide boundaries.
    /// Values are mapped to FFmpeg xfade transitions.
    /// Supported aliases: slide, zoom, fadeUp, parallax, fade, crossfade.
    /// </summary>
    public List<string> TransitionSequence { get; set; } = ["slide", "zoom", "fadeUp", "parallax"];

    /// <summary>
    /// Optional offset applied before sequence modulo selection.
    /// Useful for shifting sequence start while keeping deterministic behavior.
    /// </summary>
    public int SequenceOffset { get; set; } = 0;
}

/// <summary>
/// Configuration for the Piper neural TTS engine.
/// Required when <see cref="AudioConfig.TtsProvider"/> is <c>"Piper"</c>.
/// </summary>
public sealed class PiperConfig
{
    /// <summary>
    /// Full path to <c>piper.exe</c>.
    /// Example: <c>C:\piper\piper.exe</c>
    /// </summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the voice model file (<c>.onnx</c>).
    /// Example: <c>C:\piper\models\en_GB-alan-medium.onnx</c>
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Speaker ID for multi-speaker models. Leave <c>null</c> for single-speaker models.
    /// </summary>
    public int? Speaker { get; set; }

    /// <summary>
    /// Controls speech speed. Values below 1.0 speed up, above 1.0 slow down. Default 1.0.
    /// </summary>
    public double LengthScale { get; set; } = 1.0;

    /// <summary>
    /// Controls voice variation/expressiveness. Default 0.667.
    /// </summary>
    public double NoiseScale { get; set; } = 0.667;

    /// <summary>
    /// Controls phoneme duration variation. Default 0.8.
    /// </summary>
    public double NoiseW { get; set; } = 0.8;

    /// <summary>
    /// When true, supports pause markers in narration text such as
    /// <c>[[pause]]</c> or <c>[[pause=400]]</c>.
    /// Markers are converted to punctuation-based pacing cues before synthesis.
    /// </summary>
    public bool EnablePauseMarkers { get; set; } = true;

    /// <summary>
    /// Default pause duration (ms) used when a marker omits an explicit value,
    /// for example <c>[[pause]]</c>. Default 320 ms.
    /// </summary>
    public int PauseMarkerDefaultMs { get; set; } = 320;

    /// <summary>
    /// Minimum allowed pause duration (ms) for marker values. Default 120 ms.
    /// </summary>
    public int PauseMarkerMinMs { get; set; } = 120;

    /// <summary>
    /// Maximum allowed pause duration (ms) for marker values. Default 1200 ms.
    /// </summary>
    public int PauseMarkerMaxMs { get; set; } = 1200;
}

/// <summary>
/// Configuration for TTS narration audio generation.
/// </summary>
public sealed class AudioConfig
{
    /// <summary>
    /// Whether to generate and mux narration audio into the output MP4.
    /// Can be overridden per-run with --with-audio.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// TTS provider to use. Supported values: <c>"SystemSpeech"</c> (Windows SAPI),
    /// <c>"Piper"</c> (Piper neural TTS).
    /// </summary>
    public string TtsProvider { get; set; } = "SystemSpeech";

    /// <summary>
    /// Windows SAPI voice name (e.g. "Microsoft Zira Desktop").
    /// Set to null to use the system default voice.
    /// Run `dotnet run -- setup --list-voices` to see available voices.
    /// </summary>
    public string? VoiceName { get; set; }

    /// <summary>
    /// Speaking rate adjustment. Range: -10 (slowest) to 10 (fastest). Default 0 = normal.
    /// </summary>
    public int Rate { get; set; } = 0;

    /// <summary>Volume percentage 0–100. Default 100.</summary>
    public int Volume { get; set; } = 100;

    /// <summary>
    /// Silence to prepend before the first word of each slide's narration, in milliseconds.
    /// Gives the viewer a beat before speech starts. Default 150 ms.
    /// </summary>
    public int LeadInMs { get; set; } = 150;

    /// <summary>
    /// Silence inserted between narrated slide segments, in milliseconds. Default 300 ms.
    /// </summary>
    public int GapBetweenSlidesMs { get; set; } = 300;

    /// <summary>
    /// When true, applies FFmpeg loudnorm filter to even out audio levels.
    /// </summary>
    public bool NormalizeAudio { get; set; } = true;

    /// <summary>
    /// Piper TTS engine configuration. Required when <see cref="TtsProvider"/> is <c>"Piper"</c>.
    /// </summary>
    public PiperConfig Piper { get; set; } = new();

    /// <summary>
    /// XTTS v2 microservice configuration. Used when <see cref="TtsProvider"/> is <c>"Xtts"</c>.
    /// </summary>
    public XttsConfig Xtts { get; set; } = new();

    /// <summary>
    /// Optional background music mixed under narration.
    /// </summary>
    public BackgroundMusicConfig BackgroundMusic { get; set; } = new();
}

/// <summary>
/// Configuration for background music mixed with narration.
/// </summary>
public sealed class BackgroundMusicConfig
{
    /// <summary>
    /// When true, AppRadar attempts to mix background music under narration.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Folder containing candidate background music files.
    /// Only .wav files are considered.
    /// </summary>
    public string FolderPath { get; set; } = "backgroundMusic";

    /// <summary>
    /// Selection strategy for picking a track: "random" or "cycle".
    /// </summary>
    public string SelectionStrategy { get; set; } = "random";

    /// <summary>
    /// Background music volume multiplier used before mixing.
    /// 1.0 = unchanged, lower values are quieter.
    /// </summary>
    public double VolumeMultiplier { get; set; } = 0.16;
}

/// <summary>
/// Configuration for the local XTTS HTTP microservice.
/// </summary>
public sealed class XttsConfig
{
    /// <summary>
    /// Base URL for the local XTTS service.
    /// Example: http://localhost:8020
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8020";

    /// <summary>
    /// Relative or absolute path to the folder containing reference voice WAV files.
    /// All .wav files in this directory are used as reference audio.
    /// </summary>
    public string VoicePath { get; set; } = "voices/brand";

    /// <summary>
    /// Service endpoint path for TTS requests.
    /// </summary>
    public string TtsPath { get; set; } = "/tts";

    /// <summary>
    /// Service endpoint path for health/readiness checks.
    /// </summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>
    /// Timeout for a single TTS request in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Number of retry attempts after the initial failed request.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Initial delay in milliseconds for exponential backoff retries.
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>
    /// Time to wait for the auto-started XTTS service to become healthy.
    /// </summary>
    public int StartupWaitSeconds { get; set; } = 45;

    /// <summary>
    /// Optional startup script path for auto-starting the service.
    /// Defaults to temp/scripts/start_xtts_service.ps1.
    /// </summary>
    public string StartupScriptPath { get; set; } = "temp/scripts/start_xtts_service.ps1";

    /// <summary>
    /// Python command used when startup script is not found.
    /// </summary>
    public string PythonCommand { get; set; } = "python";

    /// <summary>
    /// Path to xtts_service.py used when startup script is not found.
    /// </summary>
    public string ScriptPath { get; set; } = "tts-service/xtts_service.py";
}

/// <summary>
/// Controls which narrative strategy the reel planner uses.
/// </summary>
public sealed class StrategyConfig
{
    /// <summary>
    /// "StructuredMarketing" (default) — intentional 4-stage Hook → PainPoint → Credibility → CTA flow.
    /// "Legacy" — original random shuffle behaviour.
    /// </summary>
    public string Mode { get; set; } = "StructuredMarketing";

    /// <summary>
    /// When true, passing --strategy legacy on the CLI is accepted and falls back to
    /// the original SelectionService shuffle logic.
    /// </summary>
    public bool AllowLegacyShuffleMode { get; set; } = true;
}

/// <summary>
/// Configuration for the local Ollama LLM endpoint.
/// </summary>
public sealed class OllamaConfig
{
    /// <summary>Base URL of the local Ollama server. Default: http://localhost:11434</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Model to use for caption rewriting.
    /// Default: qwen3:8b — change to any model available in your local Ollama installation.
    /// </summary>
    public string Model { get; set; } = "qwen3:8b";

    /// <summary>HTTP request timeout in seconds. Default 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>LLM temperature (0.0–1.0). Lower = more deterministic. Default 0.4.</summary>
    public double Temperature { get; set; } = 0.4;
}

/// <summary>
/// Configuration for the local LLM caption rewrite step.
/// </summary>
public sealed class LlmConfig
{
    /// <summary>Whether to attempt an LLM rewrite of the 4-stage draft. Default false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// LLM provider to use. Currently only "Ollama" is supported.
    /// </summary>
    public string Provider { get; set; } = "Ollama";

    /// <summary>Ollama-specific settings.</summary>
    public OllamaConfig Ollama { get; set; } = new();

    /// <summary>
    /// When true (default), falls back to a deterministic sentence join when
    /// Ollama is unavailable or returns an error. When false, the pipeline
    /// throws on LLM failure.
    /// </summary>
    public bool FallbackToDeterministicJoin { get; set; } = true;
}

/// <summary>
/// Configuration for progressive caption reveal synchronisation.
/// </summary>
public sealed class CaptionSyncConfig
{
    /// <summary>
    /// How captions are split across timeline phases.
    /// "Chunked" (default) — split narration into sentence/phrase chunks,
    /// each displayed during its proportional audio window.
    /// </summary>
    public string Mode { get; set; } = "Chunked";

    /// <summary>
    /// Silence added after the last narration word, in milliseconds.
    /// The video holds on the final frame for this duration. Default 800 ms.
    /// </summary>
    public int TailHoldMs { get; set; } = 800;

    /// <summary>
    /// Minimum total visual duration in milliseconds.
    /// If narration is shorter than this, the reel is padded to this length. Default 4000 ms.
    /// </summary>
    public int MinVisualDurationMs { get; set; } = 4000;
}

public sealed class AppConfig
{
    public VideoConfig Video { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public AnimationConfig Animation { get; set; } = new();
    public ToolsConfig Tools { get; set; } = new();
    public AudioConfig Audio { get; set; } = new();
    public StrategyConfig Strategy { get; set; } = new();
    public LlmConfig Llm { get; set; } = new();
    public CaptionSyncConfig CaptionSync { get; set; } = new();
}
