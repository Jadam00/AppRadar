using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AppRadar.Utilities;

/// <summary>
/// Generates colorful placeholder PNG images for sample/testing use.
/// Only used when actual app screenshots are not available.
/// </summary>
public static class PlaceholderImageGenerator
{
    private static readonly (int R, int G, int B)[] Gradients =
    [
        (67, 83, 233),    // Blue
        (233, 83, 67),    // Red-orange
        (67, 183, 117),   // Green
        (183, 67, 183),   // Purple
        (233, 167, 67),   // Amber
        (67, 183, 233),   // Cyan
        (233, 67, 133),   // Pink
    ];

    public static void Generate(string outputPath, string label, int width = 1080, int height = 1920)
    {
        int colorIdx = Math.Abs(label.GetHashCode()) % Gradients.Length;
        var (r, g, b) = Gradients[colorIdx];

        using var image = new Image<Rgba32>(width, height);

        image.Mutate(ctx =>
        {
            // Fill with solid color
            ctx.Fill(Color.FromRgb((byte)r, (byte)g, (byte)b));

            // Add a lighter diagonal stripe for visual interest
            var lighterColor = Color.FromRgba(
                (byte)Math.Min(r + 60, 255),
                (byte)Math.Min(g + 60, 255),
                (byte)Math.Min(b + 60, 255),
                80);

            for (int i = -height; i < width + height; i += 120)
            {
                var points = new PointF[]
                {
                    new PointF(i, 0),
                    new PointF(i + 80, 0),
                    new PointF(i + 80 + height, height),
                    new PointF(i + height, height)
                };
                ctx.FillPolygon(lighterColor, points);
            }

            // Draw centered label text if font available
            if (SystemFonts.TryGet("DejaVu Sans", out var family) ||
                SystemFonts.TryGet("Liberation Sans", out family) ||
                SystemFonts.TryGet("FreeSans", out family) ||
                SystemFonts.TryGet("Arial", out family))
            {
                var font = family.CreateFont(80, FontStyle.Bold);
                var textOptions = new RichTextOptions(font)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Origin = new System.Numerics.Vector2(width / 2f, height / 2f)
                };
                // Shadow
                var shadowOpts = new RichTextOptions(font)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Origin = new System.Numerics.Vector2(width / 2f + 4, height / 2f + 4)
                };
                ctx.DrawText(shadowOpts, label, Color.FromRgba(0, 0, 0, 120));
                ctx.DrawText(textOptions, label, Color.White);
            }
        });

        image.SaveAsPng(outputPath);
    }
}
