using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Converts a <see cref="ShapeKind.Text"/> shape's typed glyphs into an outlined
/// <see cref="ShapeKind.CombinedPath"/> shape — the vector equivalent of "Create Outlines" — so a
/// piece of text can be edited/colored/point-edited like any other freeform path once it no
/// longer needs to stay a live, re-typeable string.</summary>
public static class TextOutliner
{
    private const float FramePadding = 4;

    /// <summary>The text origin that puts the glyph bounds where the shape's alignment asks for it inside the text frame (centred
    /// by default; a single line has no ragged edge, so Justify is the same as Left).</summary>
    private static (float X, float Y) Origin(Shape textShape, SKRect bounds)
    {
        var left = (float)textShape.X;
        var top = (float)textShape.Y;
        var right = (float)(textShape.X + textShape.Width);
        var bottom = (float)(textShape.Y + textShape.Height);

        var x = textShape.TextHAlign switch
        {
            TextHorizontalAlign.Left or TextHorizontalAlign.Justify => left + FramePadding - bounds.Left,
            TextHorizontalAlign.Right => right - FramePadding - bounds.Right,
            _ => (left + right) / 2 - bounds.MidX,
        };
        var y = textShape.TextVAlign switch
        {
            TextVerticalAlign.Top => top + FramePadding - bounds.Top,
            TextVerticalAlign.Bottom => bottom - FramePadding - bounds.Bottom,
            _ => (top + bottom) / 2 - bounds.MidY,
        };
        return (x, y);
    }

    /// <summary>The live text of a <see cref="ShapeKind.Text"/> shape as ONE outline path in document space, with the
    /// shape's flips and rotation baked in (underline included). The renderer feeds it through the same fill / border /
    /// shadow / blur / fade / design-style pipeline as every other shape, so all of those settings work on text too.
    /// Returns null for empty text.</summary>
    public static SKPath? BuildTextPath(Shape textShape)
    {
        if (textShape.Kind != ShapeKind.Text || string.IsNullOrEmpty(textShape.TextContent))
        {
            return null;
        }

        var weight = textShape.TextBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = textShape.TextItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var fontFamily = string.IsNullOrWhiteSpace(textShape.TextFontFamily) ? "Segoe UI" : textShape.TextFontFamily;
        using var typeface = SKTypeface.FromFamilyName(fontFamily, weight, SKFontStyleWidth.Normal, slant);
        using var font = new SKFont(typeface, (float)textShape.TextFontSize);
        using var measurePaint = new SKPaint();

        font.MeasureText(textShape.TextContent, out var textBounds, measurePaint);
        var centerX = (float)(textShape.X + textShape.Width / 2);
        var centerY = (float)(textShape.Y + textShape.Height / 2);
        var (originX, originY) = Origin(textShape, textBounds);

        var path = font.GetTextPath(textShape.TextContent, new SKPoint(originX, originY));

        if (textShape.TextUnderline)
        {
            var thickness = (float)Math.Max(1, textShape.TextFontSize / 16);
            var underlineY = originY + (float)textShape.TextFontSize * 0.12f;
            path.AddRect(new SKRect(originX + textBounds.Left, underlineY - thickness / 2, originX + textBounds.Right, underlineY + thickness / 2));
        }

        if (textShape.FlipHorizontal)
        {
            path.Transform(SKMatrix.CreateScale(-1, 1, centerX, centerY));
        }

        if (textShape.FlipVertical)
        {
            path.Transform(SKMatrix.CreateScale(1, -1, centerX, centerY));
        }

        if (textShape.Rotation != 0)
        {
            path.Transform(SKMatrix.CreateRotationDegrees((float)textShape.Rotation, centerX, centerY));
        }

        return path;
    }

    public static Shape? ConvertToShape(Shape textShape)
    {
        if (textShape.Kind != ShapeKind.Text || string.IsNullOrEmpty(textShape.TextContent))
        {
            return null;
        }

        var weight = textShape.TextBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = textShape.TextItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var fontFamily = string.IsNullOrWhiteSpace(textShape.TextFontFamily) ? "Segoe UI" : textShape.TextFontFamily;
        using var typeface = SKTypeface.FromFamilyName(fontFamily, weight, SKFontStyleWidth.Normal, slant);
        using var font = new SKFont(typeface, (float)textShape.TextFontSize);
        using var measurePaint = new SKPaint();

        // Same centering math DrawTextShape uses, so the outline lands exactly where the live
        // text used to render.
        font.MeasureText(textShape.TextContent, out var textBounds, measurePaint);
        var centerX = (float)(textShape.X + textShape.Width / 2);
        var centerY = (float)(textShape.Y + textShape.Height / 2);
        var (originX, originY) = Origin(textShape, textBounds);

        using var outline = font.GetTextPath(textShape.TextContent, new SKPoint(originX, originY));
        var bounds = outline.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        using var normalized = new SKPath(outline);
        normalized.Offset(-bounds.Left, -bounds.Top);
        normalized.Transform(SKMatrix.CreateScale(24f / bounds.Width, 24f / bounds.Height));

        return new Shape
        {
            Kind = ShapeKind.CombinedPath,
            X = bounds.Left,
            Y = bounds.Top,
            Width = Math.Max(1, bounds.Width),
            Height = Math.Max(1, bounds.Height),
            Rotation = textShape.Rotation,
            FillColor = textShape.FillColor,
            FillEnabled = textShape.FillEnabled,
            BorderColor = textShape.BorderColor,
            BorderThickness = textShape.BorderThickness,
            Opacity = textShape.Opacity,
            CustomPathData = normalized.ToSvgPathData(),
            CustomPathStrokeOnly = false,
        };
    }
}
