using AppRadar.Config;
using AppRadar.Models;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AppRadar.Rendering;

public sealed class SlideRenderer
{
    private readonly ILogger<SlideRenderer> _logger;

    public SlideRenderer(ILogger<SlideRenderer> logger)
    {
        _logger = logger;
    }

    public string RenderSlide(
        SlideSelection slide,
        string outputImagesDir,
        AppConfig config,
        string generationId)
    {
        _logger.LogInformation("Rendering slide {Slot}: {AppName}", slide.Slot, slide.AppName);

        var outputFileName = $"{generationId}_slide{slide.Slot:D2}.png";
        var outputPath = Path.Combine(outputImagesDir, outputFileName);

        var width = config.Video.Width;
        var height = config.Video.Height;
        var overlay = config.Overlay;

        using var image = LoadAndCrop(slide.SourcePath, width, height);

        // Draw top gradient overlay for app name readability
        DrawTopGradient(image, width, overlay);

        // Draw app name at the top of the slide
        DrawAppName(image, slide.AppName, width, overlay);

        // Draw bottom gradient overlay for readability
        DrawBottomGradient(image, width, height, overlay);

        // Draw caption text
        DrawCaption(image, slide.SelectedCaption, width, height, overlay);

        image.SaveAsPng(outputPath);

        _logger.LogInformation("Slide saved to {Path}", outputPath);
        return outputPath;
    }

    private static Image<Rgba32> LoadAndCrop(string imagePath, int width, int height)
    {
        using var original = Image.Load<Rgba32>(imagePath);

        // Cover fit: scale to fill the target canvas, then center-crop
        float scaleX = (float)width / original.Width;
        float scaleY = (float)height / original.Height;
        float scale = Math.Max(scaleX, scaleY);

        int scaledW = (int)Math.Ceiling(original.Width * scale);
        int scaledH = (int)Math.Ceiling(original.Height * scale);

        original.Mutate(ctx =>
        {
            ctx.Resize(scaledW, scaledH);
            int cropX = (scaledW - width) / 2;
            int cropY = (scaledH - height) / 2;
            ctx.Crop(new Rectangle(cropX, cropY, width, height));
        });

        // Return a copy (original will be disposed)
        return original.Clone(_ => { });
    }

    private static void DrawBottomGradient(Image<Rgba32> image, int width, int height, OverlayConfig overlay)
    {
        // Draw a semi-transparent gradient from bottom, covering roughly 40% of height
        int gradientHeight = (int)(height * 0.40);
        int gradientTop = height - gradientHeight;
        byte maxAlpha = (byte)(overlay.BottomGradientOpacity * 255);

        image.Mutate(ctx =>
        {
            // Draw gradient rows bottom-to-top, getting more transparent as we go up
            for (int y = 0; y < gradientHeight; y++)
            {
                float t = (float)y / gradientHeight; // 0 = top of gradient, 1 = bottom
                float easedT = t * t; // quadratic ease-in for nicer fade
                byte alpha = (byte)(maxAlpha * easedT);
                var rect = new Rectangle(0, gradientTop + (gradientHeight - 1 - y), width, 1);
                ctx.Fill(Color.FromRgba(0, 0, 0, alpha), rect);
            }
        });
    }

    private static void DrawTopGradient(Image<Rgba32> image, int width, OverlayConfig overlay)
    {
        // Draw a semi-transparent gradient from the top, covering roughly 20% of height
        int gradientHeight = (int)(image.Height * 0.20);
        byte maxAlpha = (byte)(overlay.BottomGradientOpacity * 255);

        image.Mutate(ctx =>
        {
            for (int y = 0; y < gradientHeight; y++)
            {
                float t = (float)y / gradientHeight; // 0 = top row, 1 = bottom of gradient
                float easedT = (1f - t) * (1f - t);  // quadratic fade upward
                byte alpha = (byte)(maxAlpha * easedT);
                var rect = new Rectangle(0, y, width, 1);
                ctx.Fill(Color.FromRgba(0, 0, 0, alpha), rect);
            }
        });
    }

    private static void DrawAppName(Image<Rgba32> image, string appName, int width, OverlayConfig overlay)
    {
        var fontColor = ParseHexColor(overlay.FontColor);
        var font = ResolveFont(overlay.FontFamily, overlay.FontSize);

        float maxTextWidth = width * overlay.MaxTextWidthPercent;
        float padding = overlay.Padding;

        var textOptions = new RichTextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            WrappingLength = maxTextWidth,
            Origin = new System.Numerics.Vector2(width / 2f, padding)
        };

        image.Mutate(ctx =>
        {
            if (overlay.TextShadow)
            {
                var shadowOptions = new RichTextOptions(font)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    WrappingLength = maxTextWidth,
                    Origin = new System.Numerics.Vector2(width / 2f + 3, padding + 3)
                };
                ctx.DrawText(shadowOptions, appName, Color.FromRgba(0, 0, 0, 160));
            }

            ctx.DrawText(textOptions, appName, fontColor);
        });
    }

    private static void DrawCaption(Image<Rgba32> image, string caption, int width, int height, OverlayConfig overlay)
    {
        var fontColor = ParseHexColor(overlay.FontColor);
        var font = ResolveFont(overlay.FontFamily, overlay.FontSize);

        float maxTextWidth = width * overlay.MaxTextWidthPercent;
        float padding = overlay.Padding;

        var textOptions = new RichTextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            WrappingLength = maxTextWidth,
            Origin = new System.Numerics.Vector2(width / 2f, height - padding)
        };

        image.Mutate(ctx =>
        {
            if (overlay.TextShadow)
            {
                // Draw shadow slightly offset
                var shadowOptions = new RichTextOptions(font)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    WrappingLength = maxTextWidth,
                    Origin = new System.Numerics.Vector2(width / 2f + 3, height - padding + 3)
                };
                ctx.DrawText(shadowOptions, caption, Color.FromRgba(0, 0, 0, 160));
            }

            ctx.DrawText(textOptions, caption, fontColor);
        });
    }

    private static Font ResolveFont(string familyName, float size)
    {
        // Prefer the configured font family first, then Windows system fonts,
        // then Linux/cross-platform fallbacks for completeness.
        var families = new[]
        {
            familyName,
            "Arial",
            "Segoe UI",
            "Calibri",
            "DejaVu Sans",
            "Liberation Sans",
            "FreeSans"
        };
        foreach (var name in families)
        {
            if (SystemFonts.TryGet(name, out var family))
            {
                return family.CreateFont(size, FontStyle.Bold);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a usable font. Tried: {string.Join(", ", families)}. " +
            "On Windows, Arial is built-in and should always be available. " +
            "If you are using a custom font via the 'overlay.fontFamily' config setting, " +
            "ensure the font is installed on this machine. " +
            "You can also override with any installed font name in input\\config.json: " +
            "{ \"overlay\": { \"fontFamily\": \"Arial\" } }");
    }

    private static Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex[..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            return Color.FromRgb(r, g, b);
        }
        return Color.White;
    }
}
