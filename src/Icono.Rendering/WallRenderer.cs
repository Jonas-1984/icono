using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Draws a <see cref="ShapeKind.Wall"/>: filled band, hatch pattern clipped to it, outline and segment dimensions.</summary>
public static class WallRenderer
{
    public static void Draw(SKCanvas canvas, Shape wall, byte opacityAlpha)
    {
        using var ownOutline = WallGeometry.BuildOutline(wall);
        if (ownOutline.IsEmpty)
        {
            return;
        }

        // Where this wall meets others, the corner is closed with a mitre area and the border runs round the whole group.
        var join = WallJoiner.For(wall);
        using var joined = join?.Patch is { } patch ? ownOutline.Op(patch, SKPathOp.Union) : null;
        var outline = joined ?? ownOutline;

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

        // a wall that is part of a group leaves the border to the group's last wall, so no line runs through a joint
        if (wall.BorderThickness > 0 && (join is null || join.GroupOutline is not null))
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
            canvas.DrawPath(join?.GroupOutline ?? outline, stroke);
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

    /// <summary>The settings of one dimension of the wall (null = it follows the wall's own settings).</summary>
    public static WallDimensionOverride? FindOverride(Shape wall, int segment) =>
        wall.WallDimensionOverrides?.FirstOrDefault(o => o.Segment == segment);

    /// <summary>Which side of the walking direction the dimension numbers go: for a ring (or a nearly closed wall) outside, otherwise the
    /// left. Returns 1 for the left-hand normal, -1 for the right-hand one.</summary>
    public static float DimensionSide(Shape wall)
    {
        var points = WallGeometry.AbsolutePoints(wall);
        if (points.Count < 3)
        {
            return 1f;
        }

        double area = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }

        return Math.Abs(area) < 1 ? 1f : (area > 0 ? -1f : 1f);
    }

    /// <summary>The dimension of one segment of the wall, with the settings of that segment applied (null when it is hidden or has no length).</summary>
    public static DimensionLayout? LayoutSegmentDimension(Shape wall, int index) =>
        LayoutSegmentDimension(wall, index, WallGeometry.Segments(wall), DimensionSide(wall), DimensionStyle.For(wall));

    /// <summary>Same, for callers that lay out every segment of a wall and compute the segment list, the side and the style once.</summary>
    private static DimensionLayout? LayoutSegmentDimension(
        Shape wall, int index, List<(SKPoint From, SKPoint To)> segments, float wallSide, DimensionStyle style)
    {
        if (index < 0 || index >= segments.Count)
        {
            return null;
        }

        var custom = FindOverride(wall, index);
        if (custom is { Hidden: true })
        {
            return null;
        }

        var (from, to) = segments[index];
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1f)
        {
            return null;
        }

        var side = wallSide * (custom is { Flip: true } ? -1f : 1f);
        var face = (float)(wall.WallThickness / 2);
        var nx = -dy / length * side;
        var ny = dx / length * side;
        var p1 = new SKPoint(from.X + nx * face, from.Y + ny * face);
        var p2 = new SKPoint(to.X + nx * face, to.Y + ny * face);

        var distance = (float)(custom?.Offset ?? wall.DimOffset);
        var along = (float)Math.Clamp(custom?.TextPosition ?? wall.DimTextPosition ?? 0.5, 0, 1);
        var shift = (float)(custom?.TextShift ?? wall.DimTextShift);

        var measured = DimensionRenderer.Format(length, wall.WallDimensionUnit, wall.DimPrecision);
        var text = measured;
        if (!string.IsNullOrWhiteSpace(custom?.Text))
        {
            text = custom!.Text!.Contains("<>") ? custom.Text.Replace("<>", measured) : custom.Text;
        }

        // LayoutAligned puts the line on the left of p1 to p2: turn the pair around when the dimension side is the right-hand one.
        return side > 0
            ? DimensionRenderer.LayoutAligned(p1, p2, distance, style, text, along, shift)
            : DimensionRenderer.LayoutAligned(p2, p1, distance, style, text, 1 - along, shift);
    }

    /// <summary>The length of every segment, as a real dimension next to the wall: extension lines from the wall's outer face, a
    /// dimension line with arrowheads and the number on the line (the line is broken behind the number). Each dimension can be
    /// hidden, moved, flipped or given its own text (see <see cref="Shape.WallDimensionOverrides"/>).</summary>
    private static void DrawDimensions(SKCanvas canvas, Shape wall, byte opacityAlpha)
    {
        var segments = WallGeometry.Segments(wall);
        var side = DimensionSide(wall);
        var style = DimensionStyle.For(wall);
        var color = new SKColor(0xC0, 0x39, 0x2B).WithAlpha(opacityAlpha);
        for (var i = 0; i < segments.Count; i++)
        {
            if (LayoutSegmentDimension(wall, i, segments, side, style) is { } layout)
            {
                DimensionRenderer.DrawLayout(canvas, layout, color);
            }
        }
    }
}
