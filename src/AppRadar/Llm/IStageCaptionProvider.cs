using AppRadar.Models;

namespace AppRadar.Llm;

/// <summary>
/// Abstraction for generating short (4-8 word) on-screen captions per narrative stage.
/// </summary>
public interface IStageCaptionProvider
{
    /// <summary>Name of this provider for logging and manifest output.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Generates a short stage-specific caption suitable for on-slide overlay text.
    /// </summary>
    /// <exception cref="CaptionRewriteException">
    /// Thrown when generation fails or usable output cannot be produced.
    /// </exception>
    Task<string> GenerateCaptionAsync(
        SlideRole stage,
        ReelStoryDraft draft,
        CancellationToken cancellationToken = default);
}
