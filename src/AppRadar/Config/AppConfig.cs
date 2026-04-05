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
}

public sealed class AnimationConfig
{
    public int VerticalDriftPixels { get; set; } = 60;
    public int TransitionDurationMs { get; set; } = 600;
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
