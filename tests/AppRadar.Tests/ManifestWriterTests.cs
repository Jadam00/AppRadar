using System.Text.Json;
using AppRadar.Models;
using AppRadar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppRadar.Tests;

public sealed class ManifestWriterTests
{
    [Fact]
    public void WriteManifest_CreatesValidJsonFile()
    {
        var writer = new ManifestWriter(NullLogger<ManifestWriter>.Instance);
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        var manifest = new ReelManifest
        {
            GenerationId = "test_reel_001",
            CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SeedUsed = 42,
            DurationSeconds = 24,
            Width = 1080,
            Height = 1920,
            Fps = 30,
            TransitionStyle = "Crossfade",
            VideoPath = "output/videos/test_reel_001.mp4",
            SlidePaths = ["output/images/slide01.png", "output/images/slide02.png"],
            Slides =
            [
                new SlideSelection
                {
                    Slot = 1,
                    AppName = "Adobe Express",
                    SourceType = AppSourceType.Featured,
                    ImageName = "adobe_express.png",
                    SelectedCaption = "Design in minutes",
                    SourcePath = "input/featuredApps/images/adobe_express.png",
                    RenderedSlidePath = "output/images/slide01.png"
                }
            ]
        };

        var path = writer.WriteManifest(manifest, tmpDir);

        Assert.True(File.Exists(path));

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var deserialized = JsonSerializer.Deserialize<ReelManifest>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("test_reel_001", deserialized.GenerationId);
        Assert.Equal(42, deserialized.SeedUsed);
        Assert.Equal(24, deserialized.DurationSeconds);
        Assert.Equal(1080, deserialized.Width);
        Assert.Equal(1920, deserialized.Height);
        Assert.Equal("Crossfade", deserialized.TransitionStyle);
        Assert.Single(deserialized.Slides);
        Assert.Equal("Adobe Express", deserialized.Slides[0].AppName);
    }

    [Fact]
    public void WriteManifest_FileNameContainsGenerationId()
    {
        var writer = new ManifestWriter(NullLogger<ManifestWriter>.Instance);
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        var manifest = new ReelManifest { GenerationId = "unique_reel_xyz" };
        var path = writer.WriteManifest(manifest, tmpDir);

        Assert.Contains("unique_reel_xyz", Path.GetFileName(path));
    }
}
