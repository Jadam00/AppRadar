namespace AppRadar.Models;

public sealed class ReelManifest
{
    public string GenerationId { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public int? SeedUsed { get; set; }
    public int DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public string TransitionStyle { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public bool HasAudio { get; set; }
    public string VideoPath { get; set; } = string.Empty;
    public List<string> SlidePaths { get; set; } = new();
    public List<SlideSelection> Slides { get; set; } = new();
}
