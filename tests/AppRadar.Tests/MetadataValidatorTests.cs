using AppRadar.Models;
using AppRadar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppRadar.Tests;

public sealed class MetadataValidatorTests
{
    private static MetadataValidator CreateValidator() =>
        new(NullLogger<MetadataValidator>.Instance);

    [Fact]
    public void ValidateAndBuildSources_ThrowsOnDuplicateImageName()
    {
        var validator = CreateValidator();
        var tmpDir = CreateTempImageDir(["image1.png"]);

        var description = new AppDescription
        {
            Apps =
            [
                new AppEntry { ImageName = "image1.png", AppName = "App1", Captions = ["cap1"], Enabled = true },
                new AppEntry { ImageName = "image1.png", AppName = "App2", Captions = ["cap2"], Enabled = true }
            ]
        };

        var ex = Assert.Throws<ValidationException>(() =>
            validator.ValidateAndBuildSources(description, tmpDir, AppSourceType.Featured));

        Assert.Contains("Duplicate imageName", ex.Message);
    }

    [Fact]
    public void ValidateAndBuildSources_ThrowsOnMissingImageFile()
    {
        var validator = CreateValidator();
        var tmpDir = Path.GetTempPath(); // no images here

        var description = new AppDescription
        {
            Apps =
            [
                new AppEntry { ImageName = "nonexistent.png", AppName = "App1", Captions = ["cap1"], Enabled = true }
            ]
        };

        var ex = Assert.Throws<ValidationException>(() =>
            validator.ValidateAndBuildSources(description, tmpDir, AppSourceType.Featured));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void ValidateAndBuildSources_ThrowsOnEmptyCaptions()
    {
        var validator = CreateValidator();
        var tmpDir = CreateTempImageDir(["image1.png"]);

        var description = new AppDescription
        {
            Apps =
            [
                new AppEntry { ImageName = "image1.png", AppName = "App1", Captions = [], Enabled = true }
            ]
        };

        var ex = Assert.Throws<ValidationException>(() =>
            validator.ValidateAndBuildSources(description, tmpDir, AppSourceType.Featured));

        Assert.Contains("no captions", ex.Message);
    }

    [Fact]
    public void ValidateAndBuildSources_SkipsDisabledEntries()
    {
        var validator = CreateValidator();
        var tmpDir = CreateTempImageDir(["image1.png"]);

        var description = new AppDescription
        {
            Apps =
            [
                new AppEntry { ImageName = "image1.png", AppName = "App1", Captions = ["cap1"], Enabled = false }
            ]
        };

        var sources = validator.ValidateAndBuildSources(description, tmpDir, AppSourceType.Featured);
        Assert.Empty(sources);
    }

    [Fact]
    public void ValidateFeaturedCount_DoesNotThrowWhenEmpty()
    {
        // Featured apps are optional in single-app mode; an empty list only triggers a warning.
        var validator = CreateValidator();
        var empty = new List<AppSource>();

        // Should not throw
        validator.ValidateFeaturedCount(empty);
    }

    [Fact]
    public void ValidateFeaturedCount_AcceptsAnyNonNegativeCount()
    {
        var validator = CreateValidator();
        var two = new List<AppSource>
        {
            new() { Entry = new AppEntry(), SourceType = AppSourceType.Featured, ImagePath = "" },
            new() { Entry = new AppEntry(), SourceType = AppSourceType.Featured, ImagePath = "" }
        };

        // Should not throw for any count
        validator.ValidateFeaturedCount(two);
    }

    [Fact]
    public void ValidateMyAppsCount_ThrowsWhenEmpty()
    {
        var validator = CreateValidator();
        var ex = Assert.Throws<ValidationException>(() =>
            validator.ValidateMyAppsCount([]));
        Assert.Contains("1", ex.Message);
    }

    private static string CreateTempImageDir(string[] imageNames)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in imageNames)
        {
            // Create a minimal 1-byte file as placeholder
            File.WriteAllBytes(Path.Combine(dir, name), [0x89, 0x50, 0x4E, 0x47]);
        }
        return dir;
    }
}
