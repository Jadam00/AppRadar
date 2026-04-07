namespace AppRadar.Models;

public enum AppSourceType
{
    Featured,
    MyApp
}

public sealed class AppSource
{
    public AppEntry Entry { get; set; } = null!;
    public AppSourceType SourceType { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public Dictionary<SlideRole, List<StageImageCandidate>> StageImageCandidates { get; set; } = new();
}

public sealed class StageImageCandidate
{
    public string ImageName { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
}
