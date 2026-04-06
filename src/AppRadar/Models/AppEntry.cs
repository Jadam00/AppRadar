namespace AppRadar.Models;

public sealed class AppEntry
{
    public string ImageName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public List<string> Captions { get; set; } = new();

    // Role-based structured captions (optional — fall back to Captions when absent or empty)
    public List<string>? HookCaptions { get; set; }
    public List<string>? PainPointCaptions { get; set; }
    public List<string>? CredibilityCaptions { get; set; }
    public List<string>? CtaCaptions { get; set; }

    // Short keyword overlays (1–3 words) for each narrative stage.
    // When present, these are used as the on-screen caption instead of narration chunks.
    public string? HookKeywords { get; set; }
    public string? PainKeywords { get; set; }
    public string? CredibilityKeywords { get; set; }
    public string? CtaKeywords { get; set; }

    public List<string> Tags { get; set; } = new();
    public bool Enabled { get; set; } = true;
}
