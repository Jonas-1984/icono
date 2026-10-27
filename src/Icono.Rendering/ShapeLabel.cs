using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// Text typed INTO a shape (double-click): centred in the shape's drawn outline, wrapped to its width, drawn on top of the shape
/// in <see cref="Shape.TextColor"/> (or automatically dark/white). A <see cref="ShapeKind.Text"/> shape is not handled here — its
/// text IS the shape (see <see cref="TextOutliner"/>).
/// </summary>
public static class ShapeLabel
{
    private const double Padding = 6;

    /// <summary>Which shapes can carry a label: everything except text shapes (already text), lines and design grids.</summary>
    public static bool CanHaveLabel(Shape shape) =>
        shape.Kind is not (ShapeKind.Text or ShapeKind.DesignGrid) && shape.Points is null && !ShapeGeometryFactory.IsLineFamily(shape.Kind)
        && !ArchitecturalSymbols.IsSymbol(shape.Kind);

    public static bool HasLabel(Shape shape) => CanHaveLabel(shape) && !string.IsNullOrEmpty(shape.TextContent);

    /// <summary>The colour the label is drawn in: the chosen one, else black or white — whichever reads better on the fill.</summary>
    public static ArgbColor EffectiveColor(Shape shape)
    {
        if (shape.TextColor is { } chosen)
        {
            return chosen;
        }

        if (!shape.FillEnabled || shape.FillColor.A < 90)
        {
            return ArgbColor.FromRgb(0x1B, 0x23, 0x32);
        }

        var luminance = (0.299 * shape.FillColor.R + 0.587 * shape.FillColor.G + 0.114 * shape.FillColor.B) / 255.0;
        return luminance > 0.58 ? ArgbColor.FromRgb(0x1B, 0x23, 0x32) : ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
    }

    /// <summary>A reasonable first font size for a label in <paramref name="shape"/>: about a fifth of its smaller side, 10 to 24 px.</summary>
    public static double SuggestedFontSize(Shape shape)
    {
        var box = ConnectionRouter.GetBox(shape);
        return Math.Round(Math.Clamp(Math.Min(box.Width, box.Height) * 0.2, 10, 24));
    }

    public static void Draw(SKCanvas canvas, Shape shape, byte opacityAlpha)
    {
        if (!HasLabel(shape))
        {
            return;
        }

        using var path = BuildPath(shape);
        if (path is null)
        {
            return;
        }

        var color = EffectiveColor(shape).ToSkColor();
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = color.WithAlpha((byte)(color.Alpha * opacityAlpha / 255.0)),
        };
        canvas.DrawPath(path, paint);
    }

    /// <summary>The label's glyph outlines in document space (rotated with the shape), or null when there is nothing to draw.</summary>
    public static SKPath? BuildPath(Shape shape)
    {
        if (!HasLabel(shape))
        {
            return null;
        }

        var family = string.IsNullOrWhiteSpace(shape.TextFontFamily) ? "Segoe UI" : shape.TextFontFamily;
        using var font = new SKFont(FontCache.Get(family, shape.TextBold, shape.TextItalic), (float)Math.Max(1, shape.TextFontSize));
        using var measurePaint = new SKPaint();

        var box = ConnectionRouter.GetBox(shape);
        var maxWidth = (float)Math.Max(10, box.Width - 2 * Padding);
        var lines = Wrap(shape.TextContent!, font, measurePaint, maxWidth);

        var lineHeight = font.Spacing;
        var metrics = font.Metrics;
        var blockHeight = lineHeight * lines.Count;
        var left = (float)(box.Left + Padding);
        var right = (float)(box.Left + box.Width - Padding);
        var top = shape.TextVAlign switch
        {
            TextVerticalAlign.Top => (float)(box.Top + Padding),
            TextVerticalAlign.Bottom => (float)(box.Top + box.Height - Padding) - blockHeight,
            _ => (float)(box.Top + box.Height / 2) - blockHeight / 2,
        };

        var path = new SKPath();
        for (var i = 0; i < lines.Count; i++)
        {
            var (line, endsParagraph) = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            var width = font.MeasureText(line, measurePaint);
            var justify = shape.TextHAlign == TextHorizontalAlign.Justify && !endsParagraph && line.Contains(' ');
            var x = shape.TextHAlign switch
            {
                TextHorizontalAlign.Left or TextHorizontalAlign.Justify => left,
                TextHorizontalAlign.Right => right - width,
                _ => (left + right) / 2 - width / 2,
            };
            var baseline = top + i * lineHeight - metrics.Ascent;

            if (justify)
            {
                // Both edges flush: the words keep their own width, the gaps share what is left of the line.
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var wordsWidth = words.Sum(w => font.MeasureText(w, measurePaint));
                var gap = words.Length > 1 ? (right - left - wordsWidth) / (words.Length - 1) : 0;
                var wordX = left;
                foreach (var word in words)
                {
                    using var glyphs = font.GetTextPath(word, new SKPoint(wordX, baseline));
                    path.AddPath(glyphs);
                    wordX += font.MeasureText(word, measurePaint) + gap;
                }

                width = right - left;
            }
            else
            {
                using var glyphs = font.GetTextPath(line, new SKPoint(x, baseline));
                path.AddPath(glyphs);
            }

            if (shape.TextUnderline)
            {
                var thickness = (float)Math.Max(1, shape.TextFontSize / 16);
                var underlineY = baseline + (float)shape.TextFontSize * 0.12f;
                path.AddRect(new SKRect(x, underlineY - thickness / 2, x + width, underlineY + thickness / 2));
            }
        }

        if (shape.Rotation != 0)
        {
            var frameCenterX = (float)(shape.X + shape.Width / 2);
            var frameCenterY = (float)(shape.Y + shape.Height / 2);
            path.Transform(SKMatrix.CreateRotationDegrees((float)shape.Rotation, frameCenterX, frameCenterY));
        }

        return path;
    }

    /// <summary>Breaks the text at its own line breaks and, inside each paragraph, at spaces so no line is wider than
    /// <paramref name="maxWidth"/> (a single longer word keeps its own line). Each line says whether it is the last one of its paragraph
    /// (the line a justified text leaves ragged).</summary>
    private static List<(string Text, bool EndsParagraph)> Wrap(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        var lines = new List<(string, bool)>();

        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && font.MeasureText(candidate, paint) > maxWidth)
                {
                    lines.Add((current, false));
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            lines.Add((current, true));
        }

        return lines;
    }
}
