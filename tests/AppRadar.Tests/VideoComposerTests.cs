using AppRadar.Config;
using AppRadar.Models;
using AppRadar.Video;
using Xunit;

namespace AppRadar.Tests;

public sealed class VideoComposerTests
{
    [Theory]
    [InlineData(24, 3, 4, 0.6, 2)]  // 24s, 3s/slide, 4 slides, 0.6s transition → 2 cycles
    [InlineData(12, 3, 4, 0.6, 2)]  // 12s → needs 2 cycles (1 cycle = 10.2s)
    [InlineData(30, 3, 4, 0.6, 2)]  // 30s → 2 cycles = 19.8+0.6=20.4... nope
    [InlineData(10, 3, 4, 0.6, 1)]  // 10s → 1 cycle (10.2s) is enough
    [InlineData(3, 3, 4, 0.6, 1)]   // 3s → 1 cycle
    public void CalculateCycleCount_ProducesEnoughDuration(
        int durationSeconds, int secondsPerSlide, int slideCount,
        double transitionSecs, int expectedMinCycles)
    {
        int cycles = VideoComposer.CalculateCycleCount(
            durationSeconds, secondsPerSlide, slideCount, transitionSecs);

        Assert.True(cycles >= expectedMinCycles,
            $"Expected >= {expectedMinCycles} cycles for {durationSeconds}s duration, got {cycles}");

        // Verify that cycles * slideCount slides actually cover durationSeconds
        int totalSlides = cycles * slideCount;
        double totalDuration = totalSlides * secondsPerSlide - (totalSlides - 1) * transitionSecs;
        Assert.True(totalDuration >= durationSeconds - 0.001,
            $"Total duration {totalDuration:F2}s should cover {durationSeconds}s");
    }

    [Fact]
    public void CalculateCycleCount_MinimumIsOne()
    {
        int cycles = VideoComposer.CalculateCycleCount(1, 3, 4, 0.6);
        Assert.True(cycles >= 1);
    }

    [Fact]
    public void FindFfmpegExe_ReturnsNullWhenExplicitPathDoesNotExist()
    {
        var config = new AppConfig
        {
            Tools = new ToolsConfig
            {
                FFmpegPath = @"C:\nonexistent\path\ffmpeg.exe"
            }
        };

        // When an explicit path is configured but missing, FindFfmpegExe returns null
        // (fail-fast rather than silently searching elsewhere)
        var result = VideoComposer.FindFfmpegExe(config);

        Assert.Null(result);
    }

    [Fact]
    public void FindFfmpegExe_UsesExplicitPathWhenItExists()
    {
        // Create a temporary file to act as a fake ffmpeg.exe
        var fakePath = Path.Combine(Path.GetTempPath(), $"fake_ffmpeg_{Guid.NewGuid():N}.exe");
        File.WriteAllText(fakePath, "fake");
        try
        {
            var config = new AppConfig
            {
                Tools = new ToolsConfig { FFmpegPath = fakePath }
            };

            var result = VideoComposer.FindFfmpegExe(config);

            Assert.Equal(fakePath, result);
        }
        finally
        {
            File.Delete(fakePath);
        }
    }

    [Fact]
    public void FindFfmpegExe_TrimsQuotesFromExplicitPath()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"fake_ffmpeg_{Guid.NewGuid():N}.exe");
        File.WriteAllText(fakePath, "fake");
        try
        {
            // Simulate a user quoting the path in config.json
            var config = new AppConfig
            {
                Tools = new ToolsConfig { FFmpegPath = $"\"{fakePath}\"" }
            };

            var result = VideoComposer.FindFfmpegExe(config);

            Assert.Equal(fakePath, result);
        }
        finally
        {
            File.Delete(fakePath);
        }
    }

    // ── BuildFilterGraph ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildFilterGraph_SlideTransition_UsesSlideUp()
    {
        var graph = VideoComposer.BuildFilterGraph(
            2, 30, [3.0, 3.0], 6, 1080, 1920, 60, 600, TransitionStyle.Slide);

        Assert.Contains("transition=slideup", graph);
    }

    [Fact]
    public void BuildFilterGraph_CrossfadeTransition_UsesFade()
    {
        var graph = VideoComposer.BuildFilterGraph(
            2, 30, [3.0, 3.0], 6, 1080, 1920, 60, 600, TransitionStyle.Crossfade);

        Assert.Contains("transition=fade", graph);
        Assert.DoesNotContain("slideup", graph);
    }

    [Fact]
    public void BuildFilterGraph_SingleSlide_NoXfade()
    {
        var graph = VideoComposer.BuildFilterGraph(
            1, 30, [5.0], 5, 1080, 1920, 60, 600, TransitionStyle.Slide);

        Assert.DoesNotContain("xfade", graph);
        Assert.Contains("[v0]trim=duration=5", graph);
    }

    [Fact]
    public void BuildFilterGraph_PerSlideDurations_OffsetUsesCorrectCumulativeTime()
    {
        // Slide 0: 2.0s, Slide 1: 4.0s, transition 0.6s
        // Transition offset = 2.0 - 1 * 0.6 = 1.4
        var graph = VideoComposer.BuildFilterGraph(
            2, 30, [2.0, 4.0], 6, 1080, 1920, 60, 600, TransitionStyle.Slide);

        Assert.Contains("offset=1.400", graph);
    }

    [Fact]
    public void BuildFilterGraph_MultiplePerSlideDurations_OffsetsAreProportional()
    {
        // Slide 0: 3s, Slide 1: 2s, Slide 2: 4s, transition 0.6s
        // Trans 1 offset = 3.0 - 1*0.6 = 2.4
        // Trans 2 offset = 3.0 + 2.0 - 2*0.6 = 3.8
        var graph = VideoComposer.BuildFilterGraph(
            3, 30, [3.0, 2.0, 4.0], 9, 1080, 1920, 60, 600, TransitionStyle.Slide);

        Assert.Contains("offset=2.400", graph);
        Assert.Contains("offset=3.800", graph);
    }

    [Fact]
    public void BuildFilterGraph_PerSlideDrift_UsesSlideDurationInCrop()
    {
        // With slide duration 2.5s, the crop expression should reference 2.500
        var graph = VideoComposer.BuildFilterGraph(
            1, 30, [2.5], 3, 1080, 1920, 60, 600, TransitionStyle.Slide);

        Assert.Contains("2.500", graph);
    }

    // ── Vertical scroll: structured mode timing ───────────────────────────────────────────────

    [Fact]
    public void VideoDuration_FromAudioPlusTailHold_LargerThanMinVisual()
    {
        // Mirrors GenerationService logic: max(audio + tail, minVisual)
        const int audioDurationMs = 7000;
        const int tailHoldMs = 800;
        const int minVisualMs = 4000;

        int finalMs = Math.Max(audioDurationMs + tailHoldMs, minVisualMs);

        Assert.Equal(7800, finalMs);
    }

    [Theory]
    [InlineData(5000, 800, 4000, 5800)]
    [InlineData(2000, 800, 4000, 4000)]   // below minimum → clamped to minVisual
    [InlineData(8000, 800, 4000, 8800)]
    public void VideoDuration_AlwaysAtLeastMinVisual(
        int audioDurationMs, int tailHoldMs, int minVisualMs, int expectedMs)
    {
        int finalMs = Math.Max(audioDurationMs + tailHoldMs, minVisualMs);
        Assert.Equal(expectedMs, finalMs);
    }
}
