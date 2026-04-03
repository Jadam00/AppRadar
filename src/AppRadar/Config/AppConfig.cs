namespace AppRadar.Config;

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
}
