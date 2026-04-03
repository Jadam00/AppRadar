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
}
