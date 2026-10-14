using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Draws a <see cref="ShapeKind.DesignGrid"/>: the painted squares first (one path per colour, so a big
/// pixel picture is a handful of draw calls), then — unless switched off — the grid lines on top. The grid is clipped
/// to the shape's box, rotates/flips about its centre like every other shape.</summary>
internal static class DesignGridRenderer
{
    public static void Draw(SKCanvas canvas, Shape shape, byte alpha)
    {
        var cell = (float)Math.Max(0.5, shape.GridCellSize);
        var x = (float)shape.X;
        var y = (float)shape.Y;
        var w = (float)shape.Width;
        var h = (float)shape.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var cols = DesignGridMath.Columns(shape);
        var rows = DesignGridMath.Rows(shape);

        var centerX = x + w / 2;
        var centerY = y + h / 2;

        canvas.Save();
        if (shape.Rotation != 0)
        {
            canvas.RotateDegrees((float)shape.Rotation, centerX, centerY);
        }

        if (shape.FlipHorizontal || shape.FlipVertical)
        {
            canvas.Scale(shape.FlipHorizontal ? -1 : 1, shape.FlipVertical ? -1 : 1, centerX, centerY);
        }

        canvas.ClipRect(SKRect.Create(x, y, w, h), antialias: true);

        if (shape.GridCells is { Count: > 0 } cells)
        {
            var byColor = new Dictionary<ArgbColor, SKPath>();
            foreach (var (key, color) in cells)
            {
                var (row, col) = DesignGridMath.FromKey(key);
                if (row >= rows || col >= cols)
                {
                    continue;
                }

                if (!byColor.TryGetValue(color, out var path))
                {
                    path = new SKPath();
                    byColor[color] = path;
                }

                // Overlap neighbours by a hair so antialiasing never leaves seams between painted squares.
                path.AddRect(new SKRect(x + col * cell - 0.2f, y + row * cell - 0.2f, x + (col + 1) * cell + 0.2f, y + (row + 1) * cell + 0.2f));
            }

            foreach (var (color, path) in byColor)
            {
                using (path)
                {
                    using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Combine(color, alpha) };
                    canvas.DrawPath(path, fill);
                }
            }
        }

        if (shape.GridShowLines && shape.BorderThickness > 0)
        {
            using var lines = new SKPath();
            for (var c = 0; c <= cols; c++)
            {
                var lx = Math.Min(x + c * cell, x + w);
                lines.MoveTo(lx, y);
                lines.LineTo(lx, y + h);
            }

            for (var r = 0; r <= rows; r++)
            {
                var ly = Math.Min(y + r * cell, y + h);
                lines.MoveTo(x, ly);
                lines.LineTo(x + w, ly);
            }

            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)shape.BorderThickness,
                Color = Combine(shape.BorderColor, alpha),
            };
            canvas.DrawPath(lines, stroke);
        }

        canvas.Restore();
    }

    private static SKColor Combine(ArgbColor color, byte alpha)
    {
        var sk = color.ToSkColor();
        return sk.WithAlpha((byte)(sk.Alpha * alpha / 255.0));
    }
}
