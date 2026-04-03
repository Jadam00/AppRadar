namespace AppRadar.Models;

public sealed class AppEntry
{
    public string ImageName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public List<string> Captions { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public bool Enabled { get; set; } = true;
}
