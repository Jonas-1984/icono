using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Converts a <see cref="ShapeKind.Text"/> shape's typed glyphs into an outlined
/// <see cref="ShapeKind.CombinedPath"/> shape — the vector equivalent of "Create Outlines" — so a
/// piece of text can be edited/colored/point-edited like any other freeform path once it no
/// longer needs to stay a live, re-typeable string.</summary>
public static class TextOutliner
{
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
        var originX = centerX - textBounds.MidX;
        var originY = centerY - textBounds.MidY;

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
