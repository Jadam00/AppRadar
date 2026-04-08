using AppRadar.Config;
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

    [Fact]
    public void BuildFilterGraph_UsesDeterministicTransitionSequence()
    {
        var sequence = new List<string> { "slide", "zoom", "fadeUp", "parallax" };

        var script = VideoComposer.BuildFilterGraph(
            totalSlides: 5,
            fps: 30,
            secondsPerSlide: 3,
            durationSeconds: 15,
            width: 1080,
            height: 1920,
            transitionMs: 600,
            transition: AppRadar.Models.TransitionStyle.Slide,
            transitionSequence: sequence,
            sequenceOffset: 0);

        Assert.Contains("transition=slideleft", script, StringComparison.Ordinal);
        Assert.Contains("transition=zoomin", script, StringComparison.Ordinal);
        Assert.Contains("transition=fade", script, StringComparison.Ordinal);
        Assert.Contains("transition=smoothleft", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilterGraphChunked_SequenceOffsetShiftsTransitionSelection()
    {
        var durations = new List<double> { 2.0, 2.0, 2.0 };
        var sequence = new List<string> { "slide", "zoom", "fadeUp", "parallax" };

        var script = VideoComposer.BuildFilterGraphChunked(
            totalSlides: 3,
            fps: 30,
            durationSeconds: 6,
            width: 1080,
            height: 1920,
            transitionMs: 600,
            displayDurSec: durations,
            transitionSequence: sequence,
            sequenceOffset: 1);

        Assert.Contains("transition=zoomin", script, StringComparison.Ordinal);
        Assert.Contains("transition=fade", script, StringComparison.Ordinal);
        Assert.DoesNotContain("transition=slideleft", script, StringComparison.Ordinal);
    }
}
