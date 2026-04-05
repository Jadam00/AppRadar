using AppRadar.Config;
using AppRadar.Models;

namespace AppRadar.Services;

/// <summary>
/// Builds a <see cref="ReelNarrationPlan"/> from a narration paragraph and a measured
/// audio duration.
///
/// Splitting strategy ("Chunked"):
///   1. Split the full narration into sentence-or-phrase chunks at punctuation boundaries
///      or every N words if no punctuation boundary exists.
///   2. Map each chunk to a time window proportional to its word count vs. total words.
///   3. The last chunk's window extends to audio end + tail hold.
///
/// All timing is derived from the actual measured WAV duration, not a guessed value.
/// No chunk's reveal window extends beyond (audioDurationMs + tailHoldMs).
/// </summary>
public static class NarrationPlanner
{
    private const int WordsPerForcedChunk = 8;

    /// <summary>
    /// Builds a <see cref="ReelNarrationPlan"/> for a completed narration paragraph.
    /// </summary>
    /// <param name="narrationText">The final narration paragraph.</param>
    /// <param name="audioDurationMs">
    /// Measured WAV duration in ms.  Pass 0 to fall back to
    /// <paramref name="minVisualDurationMs"/>.
    /// </param>
    /// <param name="tailHoldMs">Silence padding appended after the last word.</param>
    /// <param name="minVisualDurationMs">
    /// Minimum total visual duration; applied when audioDurationMs is shorter.
    /// </param>
    /// <param name="isLlmRewritten">Whether the text was LLM-rewritten.</param>
    /// <param name="llmModelUsed">Model name if LLM was used; otherwise null.</param>
    public static ReelNarrationPlan Build(
        string narrationText,
        int audioDurationMs,
        int tailHoldMs,
        int minVisualDurationMs,
        bool isLlmRewritten,
        string? llmModelUsed)
    {
        var chunks = SplitIntoChunks(narrationText);

        int baseDurationMs = Math.Max(audioDurationMs, minVisualDurationMs);
        int totalMs = baseDurationMs + tailHoldMs;

        var displayChunks = BuildRevealTimeline(chunks, baseDurationMs, tailHoldMs);

        return new ReelNarrationPlan
        {
            FullNarrationText = narrationText,
            DisplayChunks = displayChunks,
            ExpectedAudioDurationMs = audioDurationMs,
            IsLlmRewritten = isLlmRewritten,
            LlmModelUsed = llmModelUsed
        };
    }

    /// <summary>
    /// Splits a narration paragraph into display chunks.
    /// Splits at ". " or "! " or "? " boundaries first, then by word count if needed.
    /// Returns at least one chunk even for very short text.
    /// </summary>
    internal static List<string> SplitIntoChunks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [string.Empty];

        var sentences = SplitAtSentenceBoundaries(text.Trim());
        var result = new List<string>();

        foreach (var sentence in sentences)
        {
            var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= WordsPerForcedChunk)
            {
                result.Add(sentence);
            }
            else
            {
                // Split long sentence into sub-chunks of WordsPerForcedChunk words
                for (int i = 0; i < words.Length; i += WordsPerForcedChunk)
                {
                    var chunk = string.Join(' ', words.Skip(i).Take(WordsPerForcedChunk));
                    result.Add(chunk);
                }
            }
        }

        return result.Where(c => !string.IsNullOrWhiteSpace(c)).DefaultIfEmpty(text).ToList();
    }

    private static List<string> SplitAtSentenceBoundaries(string text)
    {
        var result = new List<string>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c == '.' || c == '!' || c == '?') && i + 1 < text.Length && text[i + 1] == ' ')
            {
                result.Add(text[start..(i + 1)].Trim());
                start = i + 2;
            }
        }

        if (start < text.Length)
            result.Add(text[start..].Trim());

        return result.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    /// <summary>
    /// Maps display chunks to time windows proportional to word count.
    /// The last chunk's window runs to (audioDurationMs + tailHoldMs).
    /// </summary>
    internal static List<DisplayChunk> BuildRevealTimeline(
        IReadOnlyList<string> chunks,
        int audioDurationMs,
        int tailHoldMs)
    {
        if (chunks.Count == 0)
            return [];

        var wordCounts = chunks.Select(c => Math.Max(1, c.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)).ToList();
        int totalWords = wordCounts.Sum();
        int totalMs = audioDurationMs + tailHoldMs;

        var result = new List<DisplayChunk>(chunks.Count);
        int cursor = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            bool isLast = i == chunks.Count - 1;
            int chunkMs = isLast
                ? totalMs - cursor
                : (int)Math.Round((double)wordCounts[i] / totalWords * audioDurationMs);

            chunkMs = Math.Max(chunkMs, 1);
            result.Add(new DisplayChunk
            {
                Text = chunks[i],
                StartMs = cursor,
                DurationMs = chunkMs
            });
            cursor += chunkMs;
        }

        return result;
    }
}
