using AppRadar.Models;

namespace AppRadar.Llm;

/// <summary>
/// Deterministic fallback that joins the four stage texts into one narration paragraph
/// without any LLM call.
///
/// Join rule: "{Hook}. {Pain}. {Credibility}. {CTA}."
/// with duplicate period removal, normalised spacing, and trailing period ensured.
/// </summary>
public sealed class DeterministicCaptionJoiner : ICaptionRewriteProvider
{
    /// <inheritdoc />
    public string ProviderName => "DeterministicFallback";

    /// <inheritdoc />
    public Task<string> RewriteAsync(ReelStoryDraft draft, CancellationToken cancellationToken = default)
    {
        var result = Join(draft.HookText, draft.PainPointText, draft.CredibilityText, draft.CtaText);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Joins the four stage texts into one clean spoken sentence.
    /// Exposed as a static helper for direct use in tests.
    /// </summary>
    public static string Join(string hook, string pain, string credibility, string cta)
    {
        var parts = new[] { hook, pain, credibility, cta };
        var cleaned = parts
            .Select(p => p?.Trim().TrimEnd('.', '!', '?') ?? string.Empty)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (cleaned.Count == 0)
            return string.Empty;

        return string.Join(". ", cleaned) + ".";
    }
}
