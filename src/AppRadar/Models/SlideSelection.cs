namespace AppRadar.Models;

public enum TransitionStyle
{
    Crossfade,
    Slide
}

public sealed class SlideSelection
{
    public int Slot { get; set; }
    public AppSourceType SourceType { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string SelectedCaption { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string RenderedSlidePath { get; set; } = string.Empty;
}
