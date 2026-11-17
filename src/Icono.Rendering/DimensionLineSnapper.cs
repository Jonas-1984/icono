using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Where a dimension line snapped to: the point for the pointer and the stretch of the line it lined up with (to show it).</summary>
public sealed record DimensionLineSnap(SKPoint Point, SKPoint From, SKPoint To);

/// <summary>Snapping for dimension lines, as AutoCAD lines up chains of dimensions: a dimension line that is placed or dragged snaps onto the
/// line of another dimension that runs the same way, so the two stand in one row, flush. All dimensions count: those next to walls, those of
/// doors and windows and the free dimension shapes.</summary>
public static class DimensionLineSnapper
{
    /// <summary>The straight dimension lines of the document that run along the direction (as the layouts they are drawn with), except the
    /// dimension being moved: <paramref name="owner"/> with <paramref name="ownerSegment"/> for a wall (-1 = every dimension of the shape).</summary>
    public static IEnumerable<DimensionLayout> Lines(Document document, Shape? owner, int ownerSegment)
    {
        foreach (var shape in document.VisibleShapes())
        {
            if (shape.Kind == ShapeKind.Wall && shape.WallShowDimensions && shape.Points is { Count: > 1 })
            {
                var layouts = WallRenderer.AllSegmentLayouts(shape);
                for (var i = 0; i < layouts.Count; i++)
                {
                    if (ReferenceEquals(shape, owner) && (ownerSegment < 0 || i == ownerSegment))
                    {
                        continue;
                    }

                    if (layouts[i] is { } layout)
                    {
                        yield return layout;
                    }
                }
            }
            else if (ArchitecturalSymbols.IsSymbol(shape.Kind) && !ReferenceEquals(shape, owner))
            {
                foreach (var dimension in SymbolMeasurements.Layout(document, shape))
                {
                    yield return dimension.Layout;
                }
            }
            else if (shape.Kind == ShapeKind.Dimension && !ReferenceEquals(shape, owner)
                     && shape.DimKind is DimensionKind.Horizontal or DimensionKind.Vertical or DimensionKind.Aligned)
            {
                yield return DimensionRenderer.Layout(shape);
            }
        }
    }

    /// <summary>The snap of a dimension line that runs along <paramref name="direction"/> and is to pass through <paramref name="pointer"/>: the
    /// pointer moves across the line (not along it) onto the nearest other dimension line that runs the same way and lies within
    /// <paramref name="tolerance"/>. Null when there is none.</summary>
    public static DimensionLineSnap? Snap(
        Document document, Shape? owner, int ownerSegment, SKPoint pointer, SKPoint direction, float tolerance)
    {
        var length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 1e-6f)
        {
            return null;
        }

        var u = new SKPoint(direction.X / length, direction.Y / length);
        var n = new SKPoint(-u.Y, u.X);
        float Across(SKPoint p) => p.X * n.X + p.Y * n.Y;
        float Along(SKPoint p) => p.X * u.X + p.Y * u.Y;

        var here = Across(pointer);
        DimensionLayout? best = null;
        var bestDelta = float.MaxValue;
        foreach (var layout in Lines(document, owner, ownerSegment))
        {
            var dx = layout.LineTo.X - layout.LineFrom.X;
            var dy = layout.LineTo.Y - layout.LineFrom.Y;
            var other = MathF.Sqrt(dx * dx + dy * dy);
            if (other < 1f || MathF.Abs(u.X * dy - u.Y * dx) / other > 0.02f)
            {
                continue; // not a straight line, or not running the same way
            }

            var delta = Across(layout.LineFrom) - here;
            if (MathF.Abs(delta) <= tolerance && MathF.Abs(delta) < MathF.Abs(bestDelta))
            {
                best = layout;
                bestDelta = delta;
            }
        }

        if (best is null)
        {
            return SnapToCorner(document, owner, here, pointer, u, n, tolerance);
        }

        var snapped = new SKPoint(pointer.X + n.X * bestDelta, pointer.Y + n.Y * bestDelta);
        var from = Math.Min(Math.Min(Along(best.LineFrom), Along(best.LineTo)), Along(snapped));
        var to = Math.Max(Math.Max(Along(best.LineFrom), Along(best.LineTo)), Along(snapped));
        var line = Across(best.LineFrom);
        SKPoint At(float along) => new(u.X * along + n.X * line, u.Y * along + n.Y * line);
        return new DimensionLineSnap(snapped, At(from), At(to));
    }

    /// <summary>No other dimension line is near: the line is lined up with a corner of the drawing instead (a corner of a wall or of an object, a
    /// node of a wall's centre line), so that it runs exactly along the face of a neighbouring wall or through the edge of a piece of furniture.
    /// The nearest corner across the line within <paramref name="tolerance"/> wins; the guide runs through it, along the line.</summary>
    private static DimensionLineSnap? SnapToCorner(
        Document document, Shape? owner, float here, SKPoint pointer, SKPoint u, SKPoint n, float tolerance)
    {
        SKPoint? best = null;
        var bestDelta = float.MaxValue;
        foreach (var shape in document.VisibleShapes())
        {
            if (ReferenceEquals(shape, owner) || shape.Kind == ShapeKind.Dimension)
            {
                continue;
            }

            foreach (var corner in CornersOf(shape).Concat(EdgesAlong(shape, u)))
            {
                var delta = corner.X * n.X + corner.Y * n.Y - here;
                if (MathF.Abs(delta) <= tolerance && MathF.Abs(delta) < MathF.Abs(bestDelta))
                {
                    best = corner;
                    bestDelta = delta;
                }
            }
        }

        if (best is not { } feature)
        {
            return null;
        }

        var snapped = new SKPoint(pointer.X + n.X * bestDelta, pointer.Y + n.Y * bestDelta);
        var line = feature.X * n.X + feature.Y * n.Y;
        var a = feature.X * u.X + feature.Y * u.Y;
        var b = snapped.X * u.X + snapped.Y * u.Y;
        SKPoint At(float along) => new(u.X * along + n.X * line, u.Y * along + n.Y * line);
        return new DimensionLineSnap(snapped, At(Math.Min(a, b)), At(Math.Max(a, b)));
    }

    /// <summary>A point on each straight edge of the shape's outline that runs along <paramref name="u"/> (the edge of a rounded rectangle has no
    /// corner of its own but is still something to line a dimension up with).</summary>
    private static IEnumerable<SKPoint> EdgesAlong(Shape shape, SKPoint u)
    {
        foreach (var contour in SnapGeometry.Of(shape).Contours)
        {
            foreach (var (a, b, straight) in contour.Segments())
            {
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var length = MathF.Sqrt(dx * dx + dy * dy);
                if (straight && length > 1f && MathF.Abs(u.X * dy - u.Y * dx) / length <= 0.02f)
                {
                    yield return a;
                }
            }
        }
    }

    /// <summary>The corners of a shape's outline and the nodes of a wall's centre line (curves give only their ends).</summary>
    private static IEnumerable<SKPoint> CornersOf(Shape shape)
    {
        var geometry = SnapGeometry.Of(shape);
        foreach (var contour in geometry.Contours)
        {
            var count = contour.Samples.Count;
            for (var i = 0; i < count; i++)
            {
                if (!contour.Corner[i])
                {
                    continue;
                }

                // a real corner has a straight piece on each side; the ends of the arc of a rounded corner are no corner of the drawing
                var before = i > 0 ? contour.Straight[i - 1] : contour.Closed ? contour.Straight[count - 1] : true;
                var after = i < count - 1 ? contour.Straight[i] : contour.Closed ? contour.Straight[count - 1] : true;
                if (before && after)
                {
                    yield return contour.Samples[i];
                }
            }
        }

        foreach (var node in geometry.Nodes)
        {
            yield return node;
        }
    }
}
