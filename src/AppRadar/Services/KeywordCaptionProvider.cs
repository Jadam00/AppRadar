using AppRadar.Models;

namespace AppRadar.Services;

/// <summary>
/// Generates short keyword captions (1–3 words) for each narrative stage of a marketing reel.
///
/// Priority:
///   1. Explicit keyword fields on the <see cref="AppEntry"/> (e.g. <c>HookKeywords</c>).
///   2. Tag-derived short phrases when tags are available.
///   3. Deterministic per-role fallbacks.
///
/// The output is guaranteed to be non-empty and is trimmed to avoid accidental whitespace.
/// </summary>
public static class KeywordCaptionProvider
{
    /// <summary>
    /// Returns a 1–3 word keyword caption for the given narrative stage and app metadata.
    /// </summary>
    public static string GetCaptionForStage(SlideRole stage, AppEntry entry)
    {
        // 1. Explicit keyword metadata field
        var explicit_ = stage switch
        {
            SlideRole.Hook => entry.HookKeywords,
            SlideRole.PainPoint => entry.PainKeywords,
            SlideRole.Credibility => entry.CredibilityKeywords,
            SlideRole.Cta => entry.CtaKeywords,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(explicit_))
            return explicit_.Trim();

        // 2. Tag-derived phrase (uses first tag when available)
        var firstTag = entry.Tags.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim();
        if (!string.IsNullOrEmpty(firstTag))
        {
            return stage switch
            {
                SlideRole.Hook => $"Too {firstTag}?",
                SlideRole.PainPoint => $"Manual {firstTag}",
                SlideRole.Credibility => $"Smart {firstTag}",
                SlideRole.Cta => BuildCtaKeyword(entry.AppName),
                _ => BuildCtaKeyword(entry.AppName)
            };
        }

        // 3. Deterministic fallback
        return GetFallbackCaption(stage, entry.AppName);
    }

    /// <summary>
    /// Returns a deterministic fallback keyword when no app entry is available.
    /// </summary>
    public static string GetFallbackCaption(SlideRole stage, string appName) =>
        stage switch
        {
            SlideRole.Hook => "Too slow",
            SlideRole.PainPoint => "Wasting time",
            SlideRole.Credibility => "Better way",
            SlideRole.Cta => BuildCtaKeyword(appName),
            _ => BuildCtaKeyword(appName)
        };

    private static string BuildCtaKeyword(string appName)
    {
        var trimmed = (appName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "Try it";

        // "Try {appName}" fits within 3 words when appName is one word; use just the name otherwise.
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 1 ? $"Try {trimmed}" : trimmed;
    }
}
