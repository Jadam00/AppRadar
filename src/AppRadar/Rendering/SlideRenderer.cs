using AppRadar.Config;
using AppRadar.Models;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SysPath = System.IO.Path;

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
        var outputPath = SysPath.Combine(outputImagesDir, outputFileName);

        var width = config.Video.Width;
        var height = config.Video.Height;
        var overlay = config.Overlay;

        var fit = LoadAndFit(slide.SourcePath, width, height);
        using var image = fit.Canvas;

        // Measure caption first so the gradient can start where text begins.
        var captionLayout = BuildCaptionLayout(slide.SelectedCaption, overlay, fit.ContentBounds);

        // Draw bottom gradient only from caption start down to avoid over-darkening.
        DrawBottomGradient(image, fit.ContentBounds, captionLayout.TextBounds.Top, overlay);

        // Draw caption text with pill treatment.
        DrawCaption(image, slide.SelectedCaption, overlay, fit.ContentBounds, captionLayout);

        image.SaveAsPng(outputPath);

        _logger.LogInformation("Slide saved to {Path}", outputPath);
        return outputPath;
    }

    private static (Image<Rgba32> Canvas, Rectangle ContentBounds) LoadAndFit(
        string imagePath,
        int width,
        int height)
    {
        using var original = Image.Load<Rgba32>(imagePath);

        // Contain fit: scale to fit within the target canvas, then center on a solid background.
        float scaleX = (float)width / original.Width;
        float scaleY = (float)height / original.Height;
        float scale = Math.Min(scaleX, scaleY);

        int scaledW = (int)Math.Ceiling(original.Width * scale);
        int scaledH = (int)Math.Ceiling(original.Height * scale);

        original.Mutate(ctx => ctx.Resize(scaledW, scaledH));

        var canvas = new Image<Rgba32>(width, height, Color.Black);
        int offsetX = (width - scaledW) / 2;
        int offsetY = (height - scaledH) / 2;

        canvas.Mutate(ctx => ctx.DrawImage(original, new Point(offsetX, offsetY), 1f));
        var contentBounds = new Rectangle(offsetX, offsetY, scaledW, scaledH);
        return (canvas, contentBounds);
    }

    private static void DrawBottomGradient(
        Image<Rgba32> image,
        Rectangle contentBounds,
        float captionTop,
        OverlayConfig overlay)
    {
        float opacity = Math.Clamp(overlay.BottomGradientOpacity, 0f, 1f);
        if (opacity <= 0f)
            return;

        int gradientTop = (int)MathF.Floor(MathF.Max(contentBounds.Top, captionTop));
        int gradientBottom = contentBounds.Bottom;
        int gradientHeight = gradientBottom - gradientTop;
        if (gradientHeight <= 0)
            return;

        byte maxAlpha = (byte)(overlay.BottomGradientOpacity * 255);

        image.Mutate(ctx =>
        {
            for (int y = 0; y < gradientHeight; y++)
            {
                float t = gradientHeight == 1 ? 1f : (float)y / (gradientHeight - 1);
                float easedT = t * t;
                byte alpha = (byte)(maxAlpha * easedT);
                var rect = new Rectangle(contentBounds.Left, gradientTop + y, contentBounds.Width, 1);
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

    private static void DrawCaption(
        Image<Rgba32> image,
        string caption,
        OverlayConfig overlay,
        Rectangle contentBounds,
        CaptionLayout layout)
    {
        var fontColor = ParseHexColor(overlay.FontColor);

        image.Mutate(ctx =>
        {
            if (overlay.CaptionPillEnabled)
            {
                DrawCaptionPill(ctx, layout.TextBounds, contentBounds, overlay);
            }

            if (overlay.TextShadow)
            {
                ctx.DrawText(layout.ShadowOptions, caption, Color.FromRgba(0, 0, 0, 145));
            }

            ctx.DrawText(layout.TextOptions, caption, fontColor);
        });
    }

    private static CaptionLayout BuildCaptionLayout(
        string caption,
        OverlayConfig overlay,
        Rectangle contentBounds)
    {
        var font = ResolveFont(overlay.FontFamily, overlay.FontSize);
        float maxTextWidth = contentBounds.Width * overlay.MaxTextWidthPercent;
        float padding = overlay.Padding;
        float liftFactor = MathF.Max(overlay.CaptionLiftFactor, 0f);
        float captionLift = MathF.Max(padding * liftFactor, 18f);
        float captionBaselineY = contentBounds.Bottom - padding - captionLift;
        float minBaselineY = contentBounds.Top + padding;
        if (captionBaselineY < minBaselineY)
            captionBaselineY = minBaselineY;

        float captionCenterX = contentBounds.Left + contentBounds.Width / 2f;

        var textOptions = new RichTextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            WrappingLength = maxTextWidth,
            Origin = new System.Numerics.Vector2(captionCenterX, captionBaselineY)
        };

        var shadowOptions = new RichTextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            WrappingLength = maxTextWidth,
            Origin = new System.Numerics.Vector2(captionCenterX + 2, captionBaselineY + 2)
        };

        var measuredText = TextMeasurer.MeasureBounds(caption, textOptions);
        return new CaptionLayout(textOptions, shadowOptions, measuredText);
    }

    private static void DrawCaptionPill(
        IImageProcessingContext ctx,
        FontRectangle textBounds,
        Rectangle contentBounds,
        OverlayConfig overlay)
    {
        float horizontalPad = MathF.Max(overlay.CaptionPillHorizontalPadding, 0);
        float verticalPad = MathF.Max(overlay.CaptionPillVerticalPadding, 0);
        float inset = 4f;

        float left = MathF.Max(contentBounds.Left + inset, textBounds.Left - horizontalPad);
        float top = MathF.Max(contentBounds.Top + inset, textBounds.Top - verticalPad);
        float right = MathF.Min(contentBounds.Right - inset, textBounds.Right + horizontalPad);
        float bottom = MathF.Min(contentBounds.Bottom - inset, textBounds.Bottom + verticalPad);

        float width = MathF.Max(8f, right - left);
        float height = MathF.Max(8f, bottom - top);
        var pill = new RectangleF(left, top, width, height);
        var fillColor = ParseHexColor(overlay.CaptionPillColor, overlay.CaptionPillOpacity);

        ctx.Fill(fillColor, pill);

        if (overlay.CaptionPillStrokeEnabled && overlay.CaptionPillStrokeWidth > 0f)
        {
            var strokeColor = ParseHexColor(overlay.CaptionPillStrokeColor, overlay.CaptionPillStrokeOpacity);
            var pen = Pens.Solid(strokeColor, overlay.CaptionPillStrokeWidth);
            ctx.Draw(pen, pill);
        }
    }

    private readonly record struct CaptionLayout(
        RichTextOptions TextOptions,
        RichTextOptions ShadowOptions,
        FontRectangle TextBounds);

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

    private static Color ParseHexColor(string hex, float opacity = 1f)
    {
        hex = hex.TrimStart('#');
        byte alpha = (byte)(Math.Clamp(opacity, 0f, 1f) * 255);
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex[..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            return Color.FromRgba(r, g, b, alpha);
        }
        return Color.FromRgba(255, 255, 255, alpha);
    }
}
