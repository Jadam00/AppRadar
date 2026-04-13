using AppRadar.Config;
using AppRadar.Models;
using AppRadar.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AppRadar.Tests;

public sealed class SlideRendererTests
{
    [Fact]
    public void RenderSlide_BlankCaption_SucceedsWithoutCaptionDrawing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "source.png");
            using (var source = new Image<Rgba32>(320, 640, Color.CornflowerBlue))
            {
                source.SaveAsPng(sourcePath);
            }

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var renderer = new SlideRenderer(NullLogger<SlideRenderer>.Instance);
            var config = new AppConfig();
            config.Overlay.BottomGradientOpacity = 0.9f;
            config.Overlay.CaptionPillEnabled = true;

            var slide = new SlideSelection
            {
                Slot = 7,
                Role = SlideRole.Cta,
                AppName = "Test App",
                SourceType = AppSourceType.MyApp,
                ImageName = "source.png",
                SourcePath = sourcePath,
                SelectedCaption = string.Empty
            };

            var outputPath = renderer.RenderSlide(slide, outputDir, config, "test_reel");

            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
