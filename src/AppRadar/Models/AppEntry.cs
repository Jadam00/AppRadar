namespace AppRadar.Models;

using System.Text.Json.Serialization;

public sealed class AppEntry
{
    public string AppName { get; set; } = string.Empty;
    public StageContent Hook { get; set; } = new();
    public StageContent PainPoint { get; set; } = new();
    public StageContent Credibility { get; set; } = new();
    public StageContent Cta { get; set; } = new();

    // Short keyword overlays (1–3 words) for each narrative stage.
    // When present, these are used as the on-screen caption instead of narration chunks.
    public string? HookKeywords { get; set; }
    public string? PainKeywords { get; set; }
    public string? CredibilityKeywords { get; set; }
    public string? CtaKeywords { get; set; }

    public List<string> Tags { get; set; } = new();
    public bool Enabled { get; set; } = true;

    // Compatibility helpers for internal callers and tests. These are not part of the JSON schema.
    [JsonIgnore]
    public string ImageName
    {
        get => Hook.ImageNames.FirstOrDefault() ?? string.Empty;
        set
        {
            Hook.ImageNames = CreateImageList(value);
            PainPoint.ImageNames = CreateImageList(value);
            Credibility.ImageNames = CreateImageList(value);
            Cta.ImageNames = CreateImageList(value);
        }
    }

    [JsonIgnore]
    public List<string> Captions
    {
        get => Hook.Captions;
        set
        {
            var list = value ?? [];
            Hook.Captions = [..list];
            PainPoint.Captions = [..list];
            Credibility.Captions = [..list];
            Cta.Captions = [..list];
        }
    }

    [JsonIgnore]
    public List<string>? HookCaptions
    {
        get => Hook.Captions;
        set => Hook.Captions = value ?? [];
    }

    [JsonIgnore]
    public List<string>? PainPointCaptions
    {
        get => PainPoint.Captions;
        set => PainPoint.Captions = value ?? [];
    }

    [JsonIgnore]
    public List<string>? CredibilityCaptions
    {
        get => Credibility.Captions;
        set => Credibility.Captions = value ?? [];
    }

    [JsonIgnore]
    public List<string>? CtaCaptions
    {
        get => Cta.Captions;
        set => Cta.Captions = value ?? [];
    }

    private static List<string> CreateImageList(string value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];
}

public sealed class StageContent
{
    public List<string> ImageNames { get; set; } = new();
    public List<string> Captions { get; set; } = new();
}
