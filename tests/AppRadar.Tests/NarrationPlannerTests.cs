using AppRadar.Models;
using AppRadar.Services;
using Xunit;

namespace AppRadar.Tests;

public sealed class NarrationPlannerTests
{
    // ── SplitIntoChunks ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void SplitIntoChunks_SingleSentence_ReturnsOneChunk()
    {
        var chunks = NarrationPlanner.SplitIntoChunks("This is a short sentence.");
        Assert.Single(chunks);
        Assert.Equal("This is a short sentence.", chunks[0]);
    }

    [Fact]
    public void SplitIntoChunks_TwoSentences_ReturnsTwoChunks()
    {
        var chunks = NarrationPlanner.SplitIntoChunks("First sentence. Second sentence.");
        Assert.Equal(2, chunks.Count);
        Assert.Equal("First sentence.", chunks[0]);
        Assert.Equal("Second sentence.", chunks[1]);
    }

    [Fact]
    public void SplitIntoChunks_LongSentence_SplitsAtWordBoundary()
    {
        // 16 words — should split into sub-chunks of ≤8 words
        var text = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen.";
        var chunks = NarrationPlanner.SplitIntoChunks(text);
        Assert.True(chunks.Count >= 2, "Long sentence should be split into multiple chunks");
        foreach (var chunk in chunks)
        {
            var wordCount = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(wordCount <= 8, $"Chunk too long: '{chunk}'");
        }
    }

    [Fact]
    public void SplitIntoChunks_EmptyString_ReturnsOneEmptyChunk()
    {
        var chunks = NarrationPlanner.SplitIntoChunks(string.Empty);
        Assert.Single(chunks);
    }

    [Fact]
    public void SplitIntoChunks_WithExclamation_SplitsCorrectly()
    {
        var chunks = NarrationPlanner.SplitIntoChunks("Hook line! Pain line.");
        Assert.Equal(2, chunks.Count);
    }

    // ── BuildRevealTimeline ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildRevealTimeline_TotalDurationEqualsAudioPlusTailHold()
    {
        var chunks = new[] { "First chunk.", "Second chunk.", "Third chunk." }.ToList();
        const int audioDurationMs = 6000;
        const int tailHoldMs = 800;

        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, audioDurationMs, tailHoldMs);

        int totalMs = timeline.Sum(c => c.DurationMs);
        Assert.Equal(audioDurationMs + tailHoldMs, totalMs);
    }

    [Fact]
    public void BuildRevealTimeline_ChunksAreContiguous()
    {
        var chunks = new[] { "A.", "B.", "C." }.ToList();
        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, 3000, 500);

        for (int i = 1; i < timeline.Count; i++)
        {
            int expectedStart = timeline[i - 1].StartMs + timeline[i - 1].DurationMs;
            Assert.Equal(expectedStart, timeline[i].StartMs);
        }
    }

    [Fact]
    public void BuildRevealTimeline_FirstChunkStartsAtZero()
    {
        var chunks = new[] { "Start.", "End." }.ToList();
        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, 2000, 400);

        Assert.Equal(0, timeline[0].StartMs);
    }

    [Fact]
    public void BuildRevealTimeline_LastChunkEndsAtAudioPlusTailHold()
    {
        var chunks = new[] { "A.", "B." }.ToList();
        const int audioDurationMs = 4000;
        const int tailHoldMs = 800;

        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, audioDurationMs, tailHoldMs);
        var last = timeline.Last();
        Assert.Equal(audioDurationMs + tailHoldMs, last.StartMs + last.DurationMs);
    }

    [Fact]
    public void BuildRevealTimeline_NoCaptionRevealBeyondAudioPlusTailHold()
    {
        var chunks = new[] { "First.", "Second.", "Third." }.ToList();
        const int audioDurationMs = 5000;
        const int tailHoldMs = 1000;
        int totalLimit = audioDurationMs + tailHoldMs;

        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, audioDurationMs, tailHoldMs);

        foreach (var chunk in timeline)
        {
            Assert.True(chunk.StartMs + chunk.DurationMs <= totalLimit,
                $"Chunk '{chunk.Text}' extends beyond audio+tail limit");
        }
    }

    [Fact]
    public void BuildRevealTimeline_PropagatesChunkTexts()
    {
        var chunks = new[] { "Alpha.", "Beta." }.ToList();
        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, 2000, 500);

        Assert.Equal("Alpha.", timeline[0].Text);
        Assert.Equal("Beta.", timeline[1].Text);
    }

    [Fact]
    public void BuildRevealTimeline_EmptyChunks_ReturnsEmpty()
    {
        var timeline = NarrationPlanner.BuildRevealTimeline([], 2000, 500);
        Assert.Empty(timeline);
    }

    // ── Build (full plan) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_PopulatesNarrationPlan()
    {
        const string text = "This is the narration. It has two sentences.";
        var plan = NarrationPlanner.Build(text, 4000, 800, 4000, false, null);

        Assert.Equal(text, plan.FullNarrationText);
        Assert.False(plan.IsLlmRewritten);
        Assert.Null(plan.LlmModelUsed);
        Assert.True(plan.DisplayChunks.Count >= 1);
    }

    [Fact]
    public void Build_WithLlmFlag_MarksAsRewritten()
    {
        var plan = NarrationPlanner.Build("Narration.", 3000, 500, 3000, true, "qwen3:8b");

        Assert.True(plan.IsLlmRewritten);
        Assert.Equal("qwen3:8b", plan.LlmModelUsed);
    }

    [Fact]
    public void Build_WithZeroAudioDuration_UsesMinVisualDuration()
    {
        const int minVisualMs = 5000;
        var plan = NarrationPlanner.Build("Text.", 0, 800, minVisualMs, false, null);

        // Total duration should be at least minVisual + tail
        int totalMs = plan.DisplayChunks.Sum(c => c.DurationMs);
        Assert.True(totalMs >= minVisualMs,
            $"Total display time {totalMs}ms should be >= minVisual {minVisualMs}ms");
    }

    [Fact]
    public void Build_SetsExpectedAudioDurationMs()
    {
        var plan = NarrationPlanner.Build("Some text.", 3500, 500, 4000, false, null);
        Assert.Equal(3500, plan.ExpectedAudioDurationMs);
    }
}
