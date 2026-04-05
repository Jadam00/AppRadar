using AppRadar.Llm;
using AppRadar.Models;
using AppRadar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppRadar.Tests;

/// <summary>
/// Tests that verify the full single-app pipeline contract:
/// one app, one image, 4 stages preserved, narration = displayed text source.
/// </summary>
public sealed class SingleAppPipelineTests
{
    private static ReelPlanner CreatePlanner() => new(NullLogger<ReelPlanner>.Instance);

    private static AppSource MakeMyApp(string name = "PuzzleGame", string image = "puzzle.png") =>
        new()
        {
            Entry = new AppEntry
            {
                AppName = name,
                ImageName = image,
                Captions = ["Generic caption"],
                Tags = ["logic", "puzzle"],
                HookCaptions = ["Stop scrolling and try this!"],
                PainPointCaptions = ["Most puzzle apps get boring fast"],
                CredibilityCaptions = ["Genuinely gets harder as you improve"],
                CtaCaptions = ["Download now and prove it"]
            },
            SourceType = AppSourceType.MyApp,
            ImagePath = $"/fake/{image}"
        };

    // ── Single app / single image ──────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_AllSlidesSameApp_SingleAppSelected()
    {
        var planner = CreatePlanner();
        var myApps = new List<AppSource> { MakeMyApp("PuzzleGame", "puzzle.png") };
        var plan = planner.Plan([], myApps, new Random(1));

        var distinctApps = plan.Slides.Select(s => s.Source.Entry.AppName).Distinct().ToList();
        var distinctImages = plan.Slides.Select(s => s.Source.Entry.ImageName).Distinct().ToList();

        Assert.Single(distinctApps);
        Assert.Single(distinctImages);
        Assert.Equal("PuzzleGame", distinctApps[0]);
    }

    // ── 4-stage texts preserved internally ───────────────────────────────────────────────────

    [Fact]
    public void Plan_StoryDraft_ContainsAllFourStageTexts()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan([], new List<AppSource> { MakeMyApp() }, new Random(1));

        Assert.NotNull(plan.StoryDraft);
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft!.HookText));
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft.PainPointText));
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft.CredibilityText));
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft.CtaText));
    }

    [Fact]
    public void Plan_StoryDraft_PicksRoleSpecificCaptions_WhenPresent()
    {
        var planner = CreatePlanner();
        var app = MakeMyApp();
        var plan = planner.Plan([], new List<AppSource> { app }, new Random(1));

        // The stage texts should come from the role-specific caption pools
        Assert.Equal("Stop scrolling and try this!",       plan.StoryDraft!.HookText);
        Assert.Equal("Most puzzle apps get boring fast",    plan.StoryDraft.PainPointText);
        Assert.Equal("Genuinely gets harder as you improve", plan.StoryDraft.CredibilityText);
        Assert.Equal("Download now and prove it",           plan.StoryDraft.CtaText);
    }

    [Fact]
    public void Plan_SlideSlotsAreOrdered_OneToFour()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan([], new List<AppSource> { MakeMyApp() }, new Random(42));

        var roles = plan.Slides.Select(s => s.Role).ToList();
        Assert.Equal(
            new[] { SlideRole.Hook, SlideRole.PainPoint, SlideRole.Credibility, SlideRole.Cta },
            roles);
    }

    // ── Deterministic fallback join ───────────────────────────────────────────────────────────

    [Fact]
    public void DeterministicJoin_ProducesReadableNarration()
    {
        var draft = new ReelStoryDraft
        {
            HookText = "Think puzzle games are easy",
            PainPointText = "Most get boring after level five",
            CredibilityText = "NeuroMaze gets harder as you improve",
            CtaText = "Try NeuroMaze today"
        };

        var result = DeterministicCaptionJoiner.Join(
            draft.HookText, draft.PainPointText, draft.CredibilityText, draft.CtaText);

        // Must be a single non-empty string
        Assert.False(string.IsNullOrWhiteSpace(result));
        // Must contain content from each stage
        Assert.Contains("Think puzzle games are easy", result);
        Assert.Contains("Most get boring after level five", result);
        Assert.Contains("NeuroMaze gets harder as you improve", result);
        Assert.Contains("Try NeuroMaze today", result);
        // Must end with a period
        Assert.EndsWith(".", result);
    }

    // ── Narration text = displayed text source ────────────────────────────────────────────────

    [Fact]
    public void NarrationPlan_ChunkTexts_ComeFromFullNarrationText()
    {
        const string narration = "First sentence. Second sentence. Third sentence.";
        var plan = NarrationPlanner.Build(narration, 5000, 800, 4000, false, null);

        // Must have at least one non-empty chunk
        Assert.True(plan.DisplayChunks.Any(c => !string.IsNullOrWhiteSpace(c.Text)),
            "At least one display chunk must contain non-empty text");

        // Every chunk text must be a substring of the full narration (or empty)
        foreach (var chunk in plan.DisplayChunks)
        {
            if (string.IsNullOrWhiteSpace(chunk.Text)) continue;
            Assert.True(
                narration.Contains(chunk.Text, StringComparison.OrdinalIgnoreCase),
                $"Chunk '{chunk.Text}' is not derived from the narration text");
        }
    }

    [Fact]
    public void NarrationPlan_FullNarrationText_IsPreservedExactly()
    {
        const string text = "Exact narration for this reel.";
        var plan = NarrationPlanner.Build(text, 3000, 500, 3000, false, null);
        Assert.Equal(text, plan.FullNarrationText);
    }

    // ── Reveal timeline fits inside audio duration ────────────────────────────────────────────

    [Fact]
    public void NarrationPlan_RevealTimeline_FitsInsideAudioPlusTailHold()
    {
        const int audioDurationMs = 6000;
        const int tailHoldMs = 800;
        var plan = NarrationPlanner.Build(
            "Sentence one. Sentence two. Sentence three.", audioDurationMs, tailHoldMs, 4000, false, null);

        int totalLimit = audioDurationMs + tailHoldMs;
        foreach (var chunk in plan.DisplayChunks)
        {
            Assert.True(chunk.StartMs + chunk.DurationMs <= totalLimit,
                $"Chunk extends beyond limit: start={chunk.StartMs} dur={chunk.DurationMs} limit={totalLimit}");
        }
    }

    // ── Seed determinism ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_WithSameSeed_ProducesSameAppAndCaptions()
    {
        var planner = CreatePlanner();
        var myApps = Enumerable.Range(1, 5).Select(i => MakeMyApp($"App{i}", $"img{i}.png")).ToList();

        var plan1 = planner.Plan([], myApps, new Random(77));
        var plan2 = planner.Plan([], myApps, new Random(77));

        Assert.Equal(plan1.StoryDraft!.AppName,        plan2.StoryDraft!.AppName);
        Assert.Equal(plan1.StoryDraft.HookText,         plan2.StoryDraft.HookText);
        Assert.Equal(plan1.StoryDraft.PainPointText,    plan2.StoryDraft.PainPointText);
        Assert.Equal(plan1.StoryDraft.CredibilityText,  plan2.StoryDraft.CredibilityText);
        Assert.Equal(plan1.StoryDraft.CtaText,          plan2.StoryDraft.CtaText);
        Assert.Equal(plan1.Transition,                   plan2.Transition);
    }

    // ── Manifest new fields ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ReelManifest_CanCarryAllNewFields()
    {
        var manifest = new ReelManifest
        {
            GenerationId = "test_id",
            SelectedAppName = "TestApp",
            SelectedImageName = "test.png",
            HookText = "Hook",
            PainPointText = "Pain",
            CredibilityText = "Cred",
            CtaText = "CTA",
            NarrationText = "Hook. Pain. Cred. CTA.",
            LlmModelUsed = null,
            TtsProvider = "SystemSpeech",
            AudioDurationMs = 5200,
            FinalVideoDurationMs = 6000,
            LlmFallbackUsed = true,
            RevealTimeline =
            [
                new() { Text = "Hook.", StartMs = 0, DurationMs = 1500 },
                new() { Text = "CTA.",  StartMs = 1500, DurationMs = 4500 }
            ]
        };

        // All new fields must be set without exceptions
        Assert.Equal("TestApp", manifest.SelectedAppName);
        Assert.Equal("test.png", manifest.SelectedImageName);
        Assert.Equal("Hook", manifest.HookText);
        Assert.Equal("Pain", manifest.PainPointText);
        Assert.Equal("Cred", manifest.CredibilityText);
        Assert.Equal("CTA", manifest.CtaText);
        Assert.Equal("Hook. Pain. Cred. CTA.", manifest.NarrationText);
        Assert.Null(manifest.LlmModelUsed);
        Assert.Equal("SystemSpeech", manifest.TtsProvider);
        Assert.Equal(5200, manifest.AudioDurationMs);
        Assert.Equal(6000, manifest.FinalVideoDurationMs);
        Assert.True(manifest.LlmFallbackUsed);
        Assert.Equal(2, manifest.RevealTimeline!.Count);
    }

    // ── Caption source is always the final narration text ─────────────────────────────────────

    [Fact]
    public void NarrationPlan_ChunkTexts_NeverComesFromPreRewriteStageTexts()
    {
        // The 4-stage raw texts (hook/pain/credibility/cta) must NOT appear
        // as captions once a narration paragraph has been provided.
        // The narration paragraph replaces those stage texts as the caption source.
        const string hookRaw     = "HOOK_RAW_TEXT";
        const string painRaw     = "PAIN_RAW_TEXT";
        const string credRaw     = "CRED_RAW_TEXT";
        const string ctaRaw      = "CTA_RAW_TEXT";
        const string narration   = "A natural spoken sentence. And another.";

        var plan = NarrationPlanner.Build(narration, 4000, 800, 4000, false, null);

        foreach (var chunk in plan.DisplayChunks)
        {
            Assert.DoesNotContain(hookRaw, chunk.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(painRaw, chunk.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(credRaw, chunk.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ctaRaw,  chunk.Text, StringComparison.OrdinalIgnoreCase);
            // Every non-empty chunk must be a substring of the narration
            if (!string.IsNullOrWhiteSpace(chunk.Text))
                Assert.True(narration.Contains(chunk.Text, StringComparison.OrdinalIgnoreCase),
                    $"Chunk '{chunk.Text}' is not derived from narration");
        }
    }

    [Fact]
    public void NarrationPlan_FullNarrationText_IsUsedForBothTtsAndCaptions()
    {
        // The FullNarrationText must be exactly what is stored in the plan and
        // also the source of every caption chunk — no divergence.
        const string narration = "First sentence. Second sentence. Third sentence.";
        var plan = NarrationPlanner.Build(narration, 6000, 800, 4000, false, null);

        // FullNarrationText preserved exactly
        Assert.Equal(narration, plan.FullNarrationText);

        // Every display chunk is drawn from that same string
        foreach (var chunk in plan.DisplayChunks)
        {
            if (!string.IsNullOrWhiteSpace(chunk.Text))
                Assert.True(narration.Contains(chunk.Text, StringComparison.OrdinalIgnoreCase),
                    $"Caption chunk '{chunk.Text}' is not derived from the narration text");
        }
    }

    // ── Narration-length-driven timing ────────────────────────────────────────────────────────

    [Fact]
    public void NarrationPlan_TotalChunkTime_EqualsAudioPlusTailHold()
    {
        const int audioDurationMs = 5500;
        const int tailHoldMs = 800;
        var plan = NarrationPlanner.Build(
            "Alpha sentence. Beta sentence. Gamma sentence.",
            audioDurationMs, tailHoldMs, 4000, false, null);

        int totalMs = plan.DisplayChunks.Sum(c => c.DurationMs);
        Assert.True(totalMs == audioDurationMs + tailHoldMs,
            $"Total display chunk time must equal audio duration + tail hold exactly: expected {audioDurationMs + tailHoldMs}, got {totalMs}");
    }

    [Fact]
    public void NarrationPlan_WithZeroAudio_TotalTimeIsMinVisualPlusTail()
    {
        const int tailHoldMs = 800;
        const int minVisualMs = 5000;
        var plan = NarrationPlanner.Build("Short.", 0, tailHoldMs, minVisualMs, false, null);

        int totalMs = plan.DisplayChunks.Sum(c => c.DurationMs);
        Assert.Equal(minVisualMs + tailHoldMs, totalMs);
    }

    [Fact]
    public void NarrationPlan_RevealTimeline_AllChunksStartAfterOrAtPreviousEnd()
    {
        var plan = NarrationPlanner.Build(
            "First. Second. Third. Fourth.", 8000, 800, 4000, false, null);

        for (int i = 1; i < plan.DisplayChunks.Count; i++)
        {
            var prev = plan.DisplayChunks[i - 1];
            var curr = plan.DisplayChunks[i];
            int expectedStart = prev.StartMs + prev.DurationMs;
            Assert.True(curr.StartMs == expectedStart,
                $"Chunk {i} start ({curr.StartMs}) should follow previous end ({expectedStart})");
        }
    }
}
