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

public sealed class AppConfig
{
    public VideoConfig Video { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public AnimationConfig Animation { get; set; } = new();
    public ToolsConfig Tools { get; set; } = new();
}
