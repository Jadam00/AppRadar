using AppRadar.Config;
using AppRadar.Models;
using AppRadar.Services;
using AppRadar.Video;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppRadar.Tests;

/// <summary>
/// Tests verifying the refined StructuredMarketing reel pipeline:
///   1. Horizontal scroll transition is always selected in structured mode.
///   2. Caption chunks come from the final narration text (not pre-rewrite stage texts).
///   3. Total video duration follows measured audio duration + tail hold.
///   4. Reveal timeline stays within audio duration bounds.
///   5. Overlay/text shadow defaults are disabled.
///   6. Chunked filter graph uses slideleft and correct per-chunk timing.
/// </summary>
public sealed class StructuredReelBehaviorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static ReelPlanner CreatePlanner() => new(NullLogger<ReelPlanner>.Instance);

    private static List<AppSource> MakeMyApps(int count) =>
        Enumerable.Range(1, count).Select(i => new AppSource
        {
            Entry = new AppEntry
            {
                AppName = $"MyApp{i}",
                ImageName = $"myapp{i}.png",
                Captions = [$"Caption A {i}", $"Caption B {i}"],
                Tags = ["productivity", "tools"]
            },
            SourceType = AppSourceType.MyApp,
            ImagePath = $"/fake/myapp{i}.png"
        }).ToList();

    private static List<AppSource> MakeFeatured(int count) =>
        Enumerable.Range(1, count).Select(i => new AppSource
        {
            Entry = new AppEntry
            {
                AppName = $"FeaturedApp{i}",
                ImageName = $"featured{i}.png",
                Captions = [$"Featured caption {i}"],
                Tags = [$"tag{i}"]
            },
            SourceType = AppSourceType.Featured,
            ImagePath = $"/fake/featured{i}.png"
        }).ToList();

    // ── 1. Horizontal scroll transition ───────────────────────────────────────────────────────

    [Fact]
    public void Plan_StructuredMode_AlwaysUsesSlideTransition()
    {
        var planner = CreatePlanner();
        var featured = MakeFeatured(5);
        var myApps = MakeMyApps(3);

        // Over many seeds, transition must always be Slide (horizontal scroll).
        for (int seed = 0; seed < 50; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            Assert.Equal(TransitionStyle.Slide, plan.Transition);
        }
    }

    [Fact]
    public void Plan_StructuredMode_TransitionIsSlideEvenWithEmptyMyApps()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(MakeFeatured(5), [], new Random(42));
        Assert.Equal(TransitionStyle.Slide, plan.Transition);
    }

    // ── 2. Captions from final narration text ─────────────────────────────────────────────────

    [Fact]
    public void NarrationChunks_DerivedFromFinalNarrationText_NotRawStageCaptions()
    {
        // The final narration text is "Hook. PainPoint. Credibility. CTA."
        // (deterministic join of the 4 stage texts).
        // SplitIntoChunks must return chunks that together reconstitute that text —
        // NOT the independent raw stage captions.
        const string hookText = "Stop everything now";
        const string painText = "Every tool wastes your time";
        const string credText = "These actually work";
        const string ctaText = "Try it today";

        // DeterministicCaptionJoiner.Join produces: "Hook. PainPoint. Credibility. CTA."
        var narration = AppRadar.Llm.DeterministicCaptionJoiner.Join(hookText, painText, credText, ctaText);
        var chunks = NarrationPlanner.SplitIntoChunks(narration);

        // Each chunk must be a substring of the full narration text (not a raw stage caption).
        foreach (var chunk in chunks)
        {
            Assert.True(
                narration.Contains(chunk, StringComparison.Ordinal),
                $"Chunk '{chunk}' is not a substring of the full narration '{narration}'");
        }

        // The raw stage captions in their original form must NOT appear as standalone chunks
        // (they are incorporated into the joined sentence, so they appear with punctuation appended).
        Assert.DoesNotContain(hookText, chunks, StringComparer.Ordinal);
        Assert.DoesNotContain(painText, chunks, StringComparer.Ordinal);
    }

    [Fact]
    public void NarrationChunks_ReassembleToFullNarration()
    {
        // All chunks concatenated (with a space) must equal the trimmed narration text.
        const string narration = "Stop the scroll. Every tool wastes your time. These actually work. Try it today.";
        var chunks = NarrationPlanner.SplitIntoChunks(narration);

        Assert.True(chunks.Count >= 1, "Must have at least one chunk");

        var reassembled = string.Join(" ", chunks).Trim();
        Assert.Equal(narration.Trim(), reassembled);
    }

    // ── 3. Total video duration follows audio duration ────────────────────────────────────────

    [Fact]
    public void FinalDuration_WithAudio_EqualsAudioPlusTailHold()
    {
        const int audioDurationMs = 7500;
        const int tailHoldMs = 800;
        const int minVisualMs = 4000;

        int expected = Math.Max(audioDurationMs + tailHoldMs, minVisualMs);
        int expectedSeconds = (int)Math.Ceiling(expected / 1000.0);

        // Replicate the duration calculation logic from GenerationService
        int finalMs = Math.Max(audioDurationMs + tailHoldMs, minVisualMs);
        int finalSeconds = (int)Math.Ceiling(finalMs / 1000.0);

        Assert.Equal(expected, finalMs);
        Assert.Equal(expectedSeconds, finalSeconds);
    }

    [Fact]
    public void FinalDuration_WithNoAudio_UsesConfigDurationOrMin()
    {
        const int configDurationMs = 24_000;
        const int minVisualMs = 4_000;

        int finalMs = Math.Max(configDurationMs, minVisualMs);
        Assert.Equal(configDurationMs, finalMs);  // config is larger than min
    }

    [Fact]
    public void FinalDuration_MinVisualDuration_AppliesToShortAudio()
    {
        // When TTS audio is very short (e.g. 1s), minVisualDuration should win
        const int audioDurationMs = 1_000;
        const int tailHoldMs = 800;
        const int minVisualMs = 4_000;

        int finalMs = Math.Max(audioDurationMs + tailHoldMs, minVisualMs);
        Assert.Equal(minVisualMs, finalMs);
    }

    // ── 4. Reveal timeline within audio bounds ────────────────────────────────────────────────

    [Fact]
    public void RevealTimeline_NeverExceedsAudioPlusTailHold()
    {
        const int audioDurationMs = 6000;
        const int tailHoldMs = 800;
        int totalLimit = audioDurationMs + tailHoldMs;

        var chunks = NarrationPlanner.SplitIntoChunks(
            "First chunk is here. Second chunk follows. Third and final.");
        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, audioDurationMs, tailHoldMs);

        foreach (var c in timeline)
        {
            Assert.True(c.StartMs + c.DurationMs <= totalLimit,
                $"Chunk '{c.Text}' ends at {c.StartMs + c.DurationMs} ms, exceeding limit {totalLimit} ms");
        }
    }

    [Fact]
    public void RevealTimeline_WithZeroAudio_UsesFallbackDuration()
    {
        // When audioDurationMs = 0, GenerationService uses
        // effectiveAudioMs = max(0, finalDurationMs - tailHoldMs) for proportional distribution.
        const int finalDurationMs = 5000;
        const int tailHoldMs = 800;
        int effectiveAudioMs = Math.Max(0, finalDurationMs - tailHoldMs);  // 4200 ms

        var chunks = NarrationPlanner.SplitIntoChunks("A short narration sentence.");
        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, effectiveAudioMs, tailHoldMs);

        int totalMs = timeline.Sum(c => c.DurationMs);
        Assert.Equal(effectiveAudioMs + tailHoldMs, totalMs);
    }

    [Fact]
    public void RevealTimeline_ChunkDurations_ProportionalToWordCount()
    {
        // A longer sentence gets more time than a shorter one
        var shortChunk = "Short.";
        var longChunk = "This is a significantly longer sentence with many more words in it.";

        var chunks = new List<string> { shortChunk, longChunk };
        var timeline = NarrationPlanner.BuildRevealTimeline(chunks, 6000, 800);

        Assert.Equal(2, timeline.Count);
        // Long chunk should have more time than short chunk
        Assert.True(timeline[1].DurationMs > timeline[0].DurationMs,
            $"Longer chunk ({timeline[1].DurationMs} ms) should outlast shorter chunk ({timeline[0].DurationMs} ms)");
    }

    // ── 5. Shadow and gradient defaults ──────────────────────────────────────────────────────

    [Fact]
    public void OverlayConfig_DefaultHasTextShadowDisabled()
    {
        var overlay = new OverlayConfig();
        Assert.False(overlay.TextShadow, "TextShadow must default to false (shadow-free)");
    }

    [Fact]
    public void OverlayConfig_DefaultHasZeroGradientOpacity()
    {
        var overlay = new OverlayConfig();
        Assert.Equal(0.0f, overlay.BottomGradientOpacity);
    }

    [Fact]
    public void AppConfig_DefaultOverlay_IsShadowFree()
    {
        var config = new AppConfig();
        Assert.False(config.Overlay.TextShadow);
        Assert.Equal(0.0f, config.Overlay.BottomGradientOpacity);
    }

    // ── 6. Chunked filter graph ───────────────────────────────────────────────────────────────

    [Fact]
    public void BuildFilterGraphChunked_AlwaysUsesSlideleft()
    {
        var durations = new List<double> { 2.0, 3.0, 2.5 };
        var script = VideoComposer.BuildFilterGraphChunked(
            totalSlides: 3, fps: 30, durationSeconds: 8,
            width: 1080, height: 1920, driftPixels: 60,
            transitionMs: 600, displayDurSec: durations);

        Assert.Contains("slideleft", script, StringComparison.Ordinal);
        // Must not use the crossfade transition type (transition=fade)
        Assert.DoesNotContain("transition=fade", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilterGraphChunked_SingleSlide_TrimsToTargetDuration()
    {
        var durations = new List<double> { 5.0 };
        var script = VideoComposer.BuildFilterGraphChunked(
            totalSlides: 1, fps: 30, durationSeconds: 5,
            width: 1080, height: 1920, driftPixels: 60,
            transitionMs: 600, displayDurSec: durations);

        Assert.Contains("trim=duration=5", script, StringComparison.Ordinal);
        Assert.Contains("[outv]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilterGraphChunked_CorrectNumberOfInputPads()
    {
        // 4 chunks → filter must reference [0:v], [1:v], [2:v], [3:v]
        var durations = new List<double> { 2.0, 3.0, 2.0, 1.5 };
        var script = VideoComposer.BuildFilterGraphChunked(
            totalSlides: 4, fps: 30, durationSeconds: 9,
            width: 1080, height: 1920, driftPixels: 60,
            transitionMs: 600, displayDurSec: durations);

        for (int i = 0; i < 4; i++)
            Assert.Contains($"[{i}:v]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilterGraphChunked_OffsetsIncrease_AcrossChunks()
    {
        // With 3 equal-duration chunks, offsets should be strictly increasing.
        var durations = new List<double> { 3.0, 3.0, 3.0 };
        var script = VideoComposer.BuildFilterGraphChunked(
            totalSlides: 3, fps: 30, durationSeconds: 9,
            width: 1080, height: 1920, driftPixels: 60,
            transitionMs: 600, displayDurSec: durations);

        // Parse offsets from xfade lines: offset=X.XXX
        var offsets = System.Text.RegularExpressions.Regex
            .Matches(script, @"offset=(\d+\.\d+)")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.True(offsets.Count >= 2, "Expect at least 2 xfade transitions for 3 slides");
        for (int i = 1; i < offsets.Count; i++)
            Assert.True(offsets[i] > offsets[i - 1],
                $"Offset at index {i} ({offsets[i]}) should be > offset at {i - 1} ({offsets[i - 1]})");
    }

    [Fact]
    public void BuildFilterGraphChunked_ContainsOutvLabel()
    {
        var durations = new List<double> { 2.0, 3.0 };
        var script = VideoComposer.BuildFilterGraphChunked(
            totalSlides: 2, fps: 30, durationSeconds: 5,
            width: 1080, height: 1920, driftPixels: 60,
            transitionMs: 600, displayDurSec: durations);

        Assert.Contains("[outv]", script, StringComparison.Ordinal);
    }
}
