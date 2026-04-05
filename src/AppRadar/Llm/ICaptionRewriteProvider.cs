using AppRadar.Models;

namespace AppRadar.Llm;

/// <summary>
/// Abstraction for a local LLM-based caption rewrite provider.
/// Implementations take a <see cref="ReelStoryDraft"/> (4 structured marketing stages)
/// and return a single natural-sounding narration paragraph suitable for TTS.
/// </summary>
public interface ICaptionRewriteProvider
{
    /// <summary>Name of this provider for logging and manifest output.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Rewrites the four marketing stage texts in <paramref name="draft"/> into
    /// one short spoken paragraph.
    /// </summary>
    /// <param name="draft">Pre-LLM structured story draft.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rewritten narration paragraph (plain text, no bullets, no emojis).</returns>
    /// <exception cref="CaptionRewriteException">
    /// Thrown when the provider is unavailable or the request fails in a non-recoverable way.
    /// </exception>
    Task<string> RewriteAsync(ReelStoryDraft draft, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown by <see cref="ICaptionRewriteProvider"/> implementations when a rewrite
/// cannot be completed.  The caller should catch this and fall back to
/// <see cref="DeterministicCaptionJoiner"/>.
/// </summary>
public sealed class CaptionRewriteException : Exception
{
    public CaptionRewriteException(string message) : base(message) { }
    public CaptionRewriteException(string message, Exception inner) : base(message, inner) { }
}
