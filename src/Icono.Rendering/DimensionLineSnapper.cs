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
            return null;
        }

        var snapped = new SKPoint(pointer.X + n.X * bestDelta, pointer.Y + n.Y * bestDelta);
        var from = Math.Min(Math.Min(Along(best.LineFrom), Along(best.LineTo)), Along(snapped));
        var to = Math.Max(Math.Max(Along(best.LineFrom), Along(best.LineTo)), Along(snapped));
        var line = Across(best.LineFrom);
        SKPoint At(float along) => new(u.X * along + n.X * line, u.Y * along + n.Y * line);
        return new DimensionLineSnap(snapped, At(from), At(to));
    }
}
