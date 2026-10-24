using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Draws a <see cref="ShapeKind.Wall"/>: filled band, hatch pattern clipped to it, outline and segment dimensions.</summary>
public static class WallRenderer
{
    public static void Draw(SKCanvas canvas, Shape wall, byte opacityAlpha)
    {
        using var outline = WallGeometry.BuildOutline(wall);
        if (outline.IsEmpty)
        {
            return;
        }

        if (wall.FillEnabled)
        {
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Multiply(wall.FillColor, opacityAlpha) };
            canvas.DrawPath(outline, fill);
        }

        if (wall.Hatch != HatchKind.Solid)
        {
            canvas.Save();
            canvas.ClipPath(outline, SKClipOperation.Intersect, true);
            DrawHatch(canvas, outline.Bounds, wall, opacityAlpha);
            canvas.Restore();
        }

        if (wall.BorderThickness > 0)
        {
            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)wall.BorderThickness,
                StrokeJoin = SKStrokeJoin.Miter,
                StrokeMiter = 8f,
                Color = Multiply(wall.BorderColor, opacityAlpha),
            };
            canvas.DrawPath(outline, stroke);
        }

        if (wall.WallShowDimensions)
        {
            DrawDimensions(canvas, wall, opacityAlpha);
        }
    }

    private static SKColor Multiply(ArgbColor color, byte opacityAlpha)
    {
        var sk = color.ToSkColor();
        return sk.WithAlpha((byte)(sk.Alpha * opacityAlpha / 255.0));
    }

    private static void DrawHatch(SKCanvas canvas, SKRect bounds, Shape wall, byte opacityAlpha)
    {
        var spacing = (float)Math.Max(2, wall.HatchSpacing);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = Multiply(wall.HatchColor, opacityAlpha),
        };
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = paint.Color };

        var left = bounds.Left - 2;
        var top = bounds.Top - 2;
        var right = bounds.Right + 2;
        var bottom = bounds.Bottom + 2;

        void Diagonal(bool forward)
        {
            var height = bottom - top;
            for (var offset = -height; offset < right - left; offset += spacing)
            {
                if (forward)
                {
                    canvas.DrawLine(left + offset, bottom, left + offset + height, top, paint);
                }
                else
                {
                    canvas.DrawLine(left + offset, top, left + offset + height, bottom, paint);
                }
            }
        }

        void Horizontal()
        {
            for (var y = top; y < bottom; y += spacing)
            {
                canvas.DrawLine(left, y, right, y, paint);
            }
        }

        void Vertical()
        {
            for (var x = left; x < right; x += spacing)
            {
                canvas.DrawLine(x, top, x, bottom, paint);
            }
        }

        switch (wall.Hatch)
        {
            case HatchKind.Diagonal:
                Diagonal(true);
                break;
            case HatchKind.ReverseDiagonal:
                Diagonal(false);
                break;
            case HatchKind.CrossHatch:
                Diagonal(true);
                Diagonal(false);
                break;
            case HatchKind.Horizontal:
                Horizontal();
                break;
            case HatchKind.Vertical:
                Vertical();
                break;
            case HatchKind.Grid:
                Horizontal();
                Vertical();
                break;
            case HatchKind.Dots:
            {
                var row = 0;
                for (var y = top; y < bottom; y += spacing, row++)
                {
                    for (var x = left + (row % 2 == 0 ? 0 : spacing / 2); x < right; x += spacing)
                    {
                        canvas.DrawCircle(x, y, Math.Max(0.8f, spacing / 10f), fillPaint);
                    }
                }

                break;
            }

            case HatchKind.Brick:
            {
                var brickWidth = spacing * 2;
                var rowIndex = 0;
                for (var y = top; y < bottom; y += spacing, rowIndex++)
                {
                    canvas.DrawLine(left, y, right, y, paint);
                    var shift = rowIndex % 2 == 0 ? 0 : spacing;
                    for (var x = left + shift; x < right; x += brickWidth)
                    {
                        canvas.DrawLine(x, y, x, y + spacing, paint);
                    }
                }

                break;
            }

            case HatchKind.Zigzag:
            {
                for (var y = top; y < bottom; y += spacing * 1.5f)
                {
                    using var zig = new SKPath();
                    zig.MoveTo(left, y);
                    var up = false;
                    for (var x = left; x < right + spacing; x += spacing / 2)
                    {
                        zig.LineTo(x, y + (up ? -spacing / 2 : spacing / 2));
                        up = !up;
                    }

                    canvas.DrawPath(zig, paint);
                }

                break;
            }
        }
    }

    /// <summary>The length of every segment, as a real dimension next to the wall: extension lines from the wall's outer face, a
    /// dimension line with arrowheads and the number in the middle of it (the line is broken behind the number).</summary>
    private static void DrawDimensions(SKCanvas canvas, Shape wall, byte opacityAlpha)
    {
        var segments = WallGeometry.Segments(wall);
        if (segments.Count == 0)
        {
            return;
        }

        var points = WallGeometry.AbsolutePoints(wall);

        // The left-hand normal of the walking direction. For a ring that points inside when it runs clockwise on screen,
        // so flip it there: the numbers belong outside. A wall that only comes near to a ring (a cut room) is judged the same way;
        // a straight run keeps the numbers on its left.
        var side = 1f;
        if (points.Count >= 3)
        {
            double area = 0;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                area += (double)a.X * b.Y - (double)b.X * a.Y;
            }

            side = Math.Abs(area) < 1 ? 1f : (area > 0 ? -1f : 1f);
        }

        var style = DimensionStyle.For(wall);
        var color = new SKColor(0xC0, 0x39, 0x2B).WithAlpha(opacityAlpha);
        var face = (float)(wall.WallThickness / 2);
        var distance = (float)wall.DimOffset;

        foreach (var (from, to) in segments)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 1f)
            {
                continue;
            }

            // the outer face of the wall on the dimension side
            var nx = -dy / length * side;
            var ny = dx / length * side;
            var p1 = new SKPoint(from.X + nx * face, from.Y + ny * face);
            var p2 = new SKPoint(to.X + nx * face, to.Y + ny * face);

            // LayoutAligned puts the line on the left of p1 to p2; turn the pair around when the dimension side is the right one.
            var text = DimensionRenderer.Format(length, wall.WallDimensionUnit, wall.DimPrecision);
            var layout = side > 0
                ? DimensionRenderer.LayoutAligned(p1, p2, distance, style, text)
                : DimensionRenderer.LayoutAligned(p2, p1, distance, style, text);
            DimensionRenderer.DrawLayout(canvas, layout, color);
        }
    }
}
