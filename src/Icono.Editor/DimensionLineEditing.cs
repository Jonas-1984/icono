using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>Moving the dimension line of a wall (one segment) or of a door / window by dragging its number: the line follows the pointer, on either
/// side of the wall and at any distance, and lines up with the dimension lines of other dimensions that run the same way (AutoCAD's "dimension line
/// location"). Shared by the Windows program and the tablet app; the caller keeps the undo step (a <see cref="Core.ShapeSnapshot"/> before and after).</summary>
public static class DimensionLineEditing
{
    /// <summary>The direction the dimension line runs in (along the wall segment, or along the opening), or null when it has no line.</summary>
    public static SKPoint? Direction(Shape shape, int segment, SymbolDimension? symbol)
    {
        var layout = symbol?.Layout ?? (shape.Kind == ShapeKind.Wall ? WallRenderer.LayoutSegmentDimension(shape, segment) : null);
        return layout is null ? null : new SKPoint(layout.LineTo.X - layout.LineFrom.X, layout.LineTo.Y - layout.LineFrom.Y);
    }

    /// <summary>Where the pointer lands: onto the dimension line of another dimension that runs the same way when one is within
    /// <paramref name="tolerance"/> (the pointer moves across the line, not along it), else where it is. <paramref name="snap"/> tells what it lined up
    /// with, to show it.</summary>
    public static SKPoint Snapped(
        Document document, Shape shape, int segment, SKPoint pointer, SKPoint? direction, float tolerance, out DimensionLineSnap? snap)
    {
        snap = direction is { } runs
            ? DimensionLineSnapper.Snap(document, shape, shape.Kind == ShapeKind.Wall ? segment : -1, pointer, runs, tolerance)
            : null;
        return snap?.Point ?? pointer;
    }

    /// <summary>Puts the dimension line through <paramref name="point"/>. False when it cannot be placed there (no line for this dimension).</summary>
    public static bool Place(Document document, Shape shape, int segment, SKPoint point)
    {
        if (shape.Kind == ShapeKind.Wall)
        {
            if (WallRenderer.DimensionLineThrough(shape, segment, point) is not { } line)
            {
                return false;
            }

            SetWallLine(shape, segment, line.Offset, line.Flip);
            return true;
        }

        return SymbolMeasurements.PlaceDimensionLine(document, shape, point);
    }

    /// <summary>Gives the dimension of one wall segment its own distance and side (the same entry the DIMENSION settings edit).</summary>
    public static void SetWallLine(Shape wall, int segment, double offset, bool flip)
    {
        var list = (wall.WallDimensionOverrides ?? []).ToList();
        var current = list.FirstOrDefault(o => o.Segment == segment) ?? new WallDimensionOverride(segment);
        list.RemoveAll(o => o.Segment == segment);
        list.Add(current with { Offset = offset, Flip = flip });
        wall.WallDimensionOverrides = list.OrderBy(o => o.Segment).ToList();
    }
}
