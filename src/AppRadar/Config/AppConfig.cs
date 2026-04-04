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
    public float BottomGradientOpacity { get; set; } = 0.55f;
    public bool TextShadow { get; set; } = true;
}

public sealed class AnimationConfig
{
    public int VerticalDriftPixels { get; set; } = 60;
    public int TransitionDurationMs { get; set; } = 600;
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
    /// TTS provider to use. Currently supported: "SystemSpeech" (Windows only).
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

public sealed class AppConfig
{
    public VideoConfig Video { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public AnimationConfig Animation { get; set; } = new();
    public ToolsConfig Tools { get; set; } = new();
    public AudioConfig Audio { get; set; } = new();
    public StrategyConfig Strategy { get; set; } = new();
}
