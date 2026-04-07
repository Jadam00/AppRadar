namespace AppRadar.Models;

/// <summary>
/// Structured pre-LLM representation of a single-app reel story.
/// Carries the raw 4-stage marketing texts derived from the selected app's
/// caption pools before the LLM rewrite step.
/// </summary>
public sealed class ReelStoryDraft
{
    /// <summary>Name of the app this reel is promoting.</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Image file name selected for each stage.</summary>
    public Dictionary<string, string> StageImageNames { get; set; } = new();

    /// <summary>Comma-separated tags for the selected app.</summary>
    public string Tags { get; set; } = string.Empty;

    /// <summary>Stage 1 — Hook text.</summary>
    public string HookText { get; set; } = string.Empty;

    /// <summary>Stage 2 — Pain point / curiosity text.</summary>
    public string PainPointText { get; set; } = string.Empty;

    /// <summary>Stage 3 — Credibility / contrast / proof text.</summary>
    public string CredibilityText { get; set; } = string.Empty;

    /// <summary>Stage 4 — CTA / payoff text.</summary>
    public string CtaText { get; set; } = string.Empty;
}
