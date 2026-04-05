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

    public List<string> Tags { get; set; } = new();
    public bool Enabled { get; set; } = true;
}
