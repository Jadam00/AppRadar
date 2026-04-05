namespace AppRadar.Models;

/// <summary>
/// A single timed display chunk within the narration reveal timeline.
/// </summary>
public sealed class DisplayChunk
{
    /// <summary>The text visible during this chunk.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Chunk start offset from the beginning of narration, in milliseconds.</summary>
    public int StartMs { get; set; }

    /// <summary>Duration this chunk is displayed, in milliseconds.</summary>
    public int DurationMs { get; set; }
}

/// <summary>
/// Post-LLM narration plan for a single-app reel.
/// Carries the final narration paragraph, the progressive reveal timeline,
/// and timing metadata derived from the actual generated audio.
/// </summary>
public sealed class ReelNarrationPlan
{
    /// <summary>
    /// Final narration text — the exact same text used for both TTS and on-screen display.
    /// </summary>
    public string FullNarrationText { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of display chunks for the progressive caption reveal.
    /// Each chunk covers a portion of the narration and is shown during the
    /// corresponding audio window.
    /// </summary>
    public List<DisplayChunk> DisplayChunks { get; set; } = new();

    /// <summary>
    /// Expected audio duration in milliseconds — populated from the actual measured
    /// WAV file after TTS generation.
    /// </summary>
    public int ExpectedAudioDurationMs { get; set; }

    /// <summary>
    /// Whether the narration text was produced by the LLM rewrite step (true)
    /// or by the deterministic fallback join (false).
    /// </summary>
    public bool IsLlmRewritten { get; set; }

    /// <summary>
    /// The LLM model used for the rewrite, or null when the fallback was used.
    /// </summary>
    public string? LlmModelUsed { get; set; }
}
