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

    /// <summary>The side of the wall (1 = left of the walking direction, -1 = right) its dimension lines run on. The "outside" is the side the
    /// numbers have always had (for a ring: away from the room), the "inside" the other one; the placement of the wall decides, and
    /// automatically it follows what is measured.</summary>
    public static float LineSide(Shape wall)
    {
        var outside = DimensionSide(wall);
        return wall.WallDimensionPlacement switch
        {
            WallDimensionPlacement.Inside => -outside,
            WallDimensionPlacement.Outside => outside,
            _ => wall.WallDimensionMeasure == WallMeasure.Inside && wall.WallClosed ? -outside : outside,
        };
    }

    /// <summary>The wall face an inside or outside dimension measures: for a ring the inner or outer face, for an open wall the face the dimension
    /// line is on.</summary>
    public static float FaceSide(Shape wall, float lineSide)
    {
        if (!wall.WallClosed)
        {
            return lineSide;
        }

        var outside = DimensionSide(wall);
        return wall.WallDimensionMeasure == WallMeasure.Inside ? -outside : outside;
    }

    /// <summary>The dimension of one segment of the wall, with the settings of that segment applied and, where it would be cramped, moved to
    /// the outside of the plan (null when it is hidden or has no length).</summary>
    public static DimensionLayout? LayoutSegmentDimension(Shape wall, int index)
    {
        var layouts = ResolveLayouts(wall);
        return index >= 0 && index < layouts.Count ? layouts[index] : null;
    }

    /// <summary>Lays out the dimension of every segment. A dimension that would end up in a tight spot - its number on top of a wall or on top of
    /// another number, as in the pocket of a column - is moved out of the plan: its line runs outside the outermost wall, like the
    /// other dimensions of that side, with extension lines reaching in. Dimensions with an offset or side of their own are left where
    /// the user put them.</summary>
    private static List<DimensionLayout?> ResolveLayouts(Shape wall)
    {
        var segments = WallGeometry.Segments(wall);
        var side = LineSide(wall);
        var style = DimensionStyle.For(wall);
        var layouts = new List<DimensionLayout?>(segments.Count);
        using var band = segments.Count > 2 ? WallGeometry.BuildOutline(wall) : null;
        using var plan = segments.Count > 2 ? WallGeometry.BuildCenterLine(wall) : null;
        var placed = new List<SKPoint[]>();

        for (var i = 0; i < segments.Count; i++)
        {
            var layout = LayoutSegmentDimension(wall, i, segments, side, style);
            var custom = FindOverride(wall, i);
            if (wall.WallDimensionPlacement == WallDimensionPlacement.Automatic && layout is { Text.Length: > 0 } && custom?.Offset is null &&
                custom is not { Flip: true } && IsCramped(layout, band, placed))
            {
                // 1. outside the plan, where its extension lines do not have to run far through a room
                // 2. otherwise on the other side of its wall
                // 3. otherwise outside the plan anyway, however far the extension lines have to run
                var outside = LayoutOutsidePlan(wall, i, segments, style, plan, band, MaxCrossing);
                var other = LayoutSegmentDimension(wall, i, segments, -side, style);
                if (outside is { Text.Length: > 0 } && !IsCramped(outside, band, placed))
                {
                    layout = outside;
                }
                else if (other is { Text.Length: > 0 } && !IsCramped(other, band, placed))
                {
                    layout = other;
                }
                else if (LayoutOutsidePlan(wall, i, segments, style, plan, band, float.MaxValue) is { } far)
                {
                    layout = far;
                }
            }

            if (layout is { Text.Length: > 0 })
            {
                placed.Add(TextCorners(layout));
            }

            layouts.Add(layout);
        }

        return layouts;
    }

    /// <summary>Same as above for one segment, with the segment list, side and style computed by the caller.</summary>
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
        var centreLength = MathF.Sqrt(dx * dx + dy * dy);
        if (centreLength < 1f)
        {
            return null;
        }

        var side = wallSide * (custom is { Flip: true } ? -1f : 1f);
        var face = (float)(wall.WallThickness / 2);
        SKPoint p1;
        SKPoint p2;
        if (wall.WallDimensionMeasure == WallMeasure.Centre)
        {
            var nx = -dy / centreLength * side;
            var ny = dx / centreLength * side;
            p1 = new SKPoint(from.X + nx * face, from.Y + ny * face);
            p2 = new SKPoint(to.X + nx * face, to.Y + ny * face);
        }
        else
        {
            // the dimension measures a whole face, from corner to corner of that face
            (p1, p2) = WallGeometry.FacePoints(wall, index, FaceSide(wall, side));
        }

        var length = Geo.Distance(p1, p2);
        if (length < 1f)
        {
            return null;
        }

        var distance = (float)(custom?.Offset ?? wall.DimOffset);
        if (wall.WallDimensionMeasure != WallMeasure.Centre && FaceSide(wall, side) != side)
        {
            distance += (float)wall.WallThickness; // the measured face is the far one: the extension lines cross the wall
        }

        var along = (float)Math.Clamp(custom?.TextPosition ?? wall.DimTextPosition ?? 0.5, 0, 1);
        var shift = (float)(custom?.TextShift ?? wall.DimTextShift);
        var text = SegmentText(wall, custom, length);

        // LayoutAligned puts the line on the left of p1 to p2: turn the pair around when the dimension side is the right-hand one.
        return side > 0
            ? DimensionRenderer.LayoutAligned(p1, p2, distance, style, text, along, shift)
            : DimensionRenderer.LayoutAligned(p2, p1, distance, style, text, 1 - along, shift);
    }

    private static string SegmentText(Shape wall, WallDimensionOverride? custom, float length)
    {
        var measured = DimensionRenderer.Format(length, wall.WallDimensionUnit, wall.DimPrecision);
        if (string.IsNullOrWhiteSpace(custom?.Text))
        {
            return measured;
        }

        return custom!.Text!.Contains("<>") ? custom.Text.Replace("<>", measured) : custom.Text;
    }

    /// <summary>The four corners of the written number (turned with the line it sits on), a little larger than the glyphs.</summary>
    private static SKPoint[] TextCorners(DimensionLayout layout, float extra = 0f)
    {
        var halfWidth = layout.TextWidth / 2 + 2 + extra;
        var halfHeight = layout.FontSize * 0.6f + extra;
        var radians = layout.TextAngle * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);

        SKPoint Corner(float x, float y) =>
            new(layout.TextPosition.X + x * cos - y * sin, layout.TextPosition.Y + x * sin + y * cos);

        return [Corner(-halfWidth, -halfHeight), Corner(halfWidth, -halfHeight), Corner(halfWidth, halfHeight), Corner(-halfWidth, halfHeight)];
    }

    /// <summary>Points spread over the number and a margin round it, for testing what it lies on.</summary>
    private static IEnumerable<SKPoint> TextSamples(DimensionLayout layout, float extra)
    {
        var halfWidth = layout.TextWidth / 2 + 2 + extra;
        var halfHeight = layout.FontSize * 0.6f + extra;
        var radians = layout.TextAngle * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        for (var x = -halfWidth; x <= halfWidth + 0.1f; x += 4f)
        {
            foreach (var y in new[] { -halfHeight, 0f, halfHeight })
            {
                yield return new SKPoint(layout.TextPosition.X + x * cos - y * sin, layout.TextPosition.Y + x * sin + y * cos);
            }
        }
    }

    /// <summary>The number lies on a wall or on another dimension's number.</summary>
    private static bool IsCramped(DimensionLayout layout, SKPath? band, List<SKPoint[]> placed)
    {
        var corners = TextCorners(layout);

        if (band is not null)
        {
            // a little room around the number: a number that touches a wall edge is as hard to read as one on top of it
            if (TextSamples(layout, extra: 5f).Any(c => band.Contains(c.X, c.Y)))
            {
                return true;
            }
        }

        var box = Bounds(corners);
        return placed.Any(other => box.IntersectsWith(Bounds(other)));
    }

    /// <summary>The longest an extension line may run through the inside of a room (px) before a dimension is not moved outside the plan.</summary>
    private const float MaxCrossing = 40f;

    /// <summary>How far (px) the line from <paramref name="start"/> in direction <paramref name="direction"/> runs through the inside of the plan: inside
    /// the outline of the wall but not in the wall itself.</summary>
    private static float InsideRoom(SKPoint start, SKPoint direction, float reach, SKPath plan, SKPath band)
    {
        const float Step = 3f;
        var inside = 0f;
        for (var d = Step; d < reach; d += Step)
        {
            var x = start.X + direction.X * d;
            var y = start.Y + direction.Y * d;
            if (plan.Contains(x, y) && !band.Contains(x, y))
            {
                inside += Step;
            }
        }

        return inside;
    }

    private static SKRect Bounds(SKPoint[] points) =>
        new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));

    /// <summary>The dimension of a horizontal or vertical segment with its line outside the whole plan, on the side of the plan the segment
    /// is nearer to. Null for a slanted segment.</summary>
    private static DimensionLayout? LayoutOutsidePlan(
        Shape wall, int index, List<(SKPoint From, SKPoint To)> segments, DimensionStyle style, SKPath? plan, SKPath? band, float maxCrossing)
    {
        var (from, to) = segments[index];
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        var horizontal = MathF.Abs(dy) <= length * 0.02f;
        var vertical = MathF.Abs(dx) <= length * 0.02f;
        if (!horizontal && !vertical)
        {
            return null;
        }

        var measured = wall.WallDimensionMeasure == WallMeasure.Centre ? (From: from, To: to) : WallGeometry.FacePoints(wall, index, FaceSide(wall, LineSide(wall)));
        var measuredLength = wall.WallDimensionMeasure == WallMeasure.Centre ? length : Geo.Distance(measured.From, measured.To);
        var points = WallGeometry.AbsolutePoints(wall);
        float minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);

        var custom = FindOverride(wall, index);
        var face = (float)(wall.WallThickness / 2);
        var distance = (float)(custom?.Offset ?? wall.DimOffset);
        var along = (float)Math.Clamp(custom?.TextPosition ?? wall.DimTextPosition ?? 0.5, 0, 1);
        var shift = (float)(custom?.TextShift ?? wall.DimTextShift);

        SKPoint start1;
        SKPoint start2;
        SKPoint toward; // the direction the dimension line lies in, seen from the segment
        float reach;    // how far from the segment's face the line runs
        if (horizontal)
        {
            var y = (from.Y + to.Y) / 2;
            var above = y <= (minY + maxY) / 2;
            var startY = above ? y - face : y + face;
            var lineY = above ? minY - face - distance : maxY + face + distance;
            start1 = new SKPoint(measured.From.X, startY);
            start2 = new SKPoint(measured.To.X, startY);
            toward = new SKPoint(0, above ? -1 : 1);
            reach = MathF.Abs(startY - lineY);
        }
        else
        {
            var x = (from.X + to.X) / 2;
            var left = x <= (minX + maxX) / 2;
            var startX = left ? x - face : x + face;
            var lineX = left ? minX - face - distance : maxX + face + distance;
            start1 = new SKPoint(startX, measured.From.Y);
            start2 = new SKPoint(startX, measured.To.Y);
            toward = new SKPoint(left ? -1 : 1, 0);
            reach = MathF.Abs(startX - lineX);
        }

        // the extension lines must not run a long way through a room: then the dimension is better placed next to its wall
        if (plan is not null && band is not null &&
            (InsideRoom(start1, toward, reach, plan, band) > maxCrossing || InsideRoom(start2, toward, reach, plan, band) > maxCrossing))
        {
            return null;
        }

        // LayoutAligned puts the line on the left of its first point to its second: choose the order that points toward the outside.
        var normal = new SKPoint(-(start2.Y - start1.Y), start2.X - start1.X);
        var text = SegmentText(wall, custom, measuredLength);
        return normal.X * toward.X + normal.Y * toward.Y >= 0
            ? DimensionRenderer.LayoutAligned(start1, start2, reach, style, text, along, shift)
            : DimensionRenderer.LayoutAligned(start2, start1, reach, style, text, 1 - along, shift);
    }

    /// <summary>Which dimension number of the wall lies at the point (the segment index), or null. Used to edit a length by clicking it.</summary>
    public static int? HitTestDimensionText(Shape wall, double x, double y, double tolerance)
    {
        if (!wall.WallShowDimensions)
        {
            return null;
        }

        var layouts = ResolveLayouts(wall);
        for (var i = 0; i < layouts.Count; i++)
        {
            if (layouts[i] is not { Text.Length: > 0 } layout)
            {
                continue;
            }

            var radians = -layout.TextAngle * Math.PI / 180;
            var dx = x - layout.TextPosition.X;
            var dy = y - layout.TextPosition.Y;
            var localX = dx * Math.Cos(radians) - dy * Math.Sin(radians);
            var localY = dx * Math.Sin(radians) + dy * Math.Cos(radians);
            if (Math.Abs(localX) <= layout.TextWidth / 2 + tolerance && Math.Abs(localY) <= layout.FontSize * 0.7 + tolerance)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>The length of every segment, as a real dimension next to the wall: extension lines from the wall's outer face, a
    /// dimension line with arrowheads and the number on the line (the line is broken behind the number). Each dimension can be
    /// hidden, moved, flipped or given its own text (see <see cref="Shape.WallDimensionOverrides"/>). Dimensions in tight spots
    /// run outside the plan (see <see cref="ResolveLayouts"/>).</summary>
    private static void DrawDimensions(SKCanvas canvas, Shape wall, byte opacityAlpha)
    {
        var color = new SKColor(0xC0, 0x39, 0x2B).WithAlpha(opacityAlpha);
        foreach (var layout in ResolveLayouts(wall))
        {
            if (layout is not null)
            {
                DimensionRenderer.DrawLayout(canvas, layout, color);
            }
        }
    }
}
