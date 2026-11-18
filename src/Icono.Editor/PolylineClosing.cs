using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>Joins the two ends of a polyline: it becomes a closed <see cref="ShapeKind.Polygon"/> that takes a fill colour, a border and the other looks like any
/// shape. Done when the first point is tapped while drawing a polyline, by the Close key of the drawing bar, or later for a polyline that is selected.</summary>
public static class PolylineClosing
{
    /// <summary>True when the shape is an open polyline with at least three points (two points are only a line).</summary>
    public static bool CanClose(Shape shape) => shape is { Kind: ShapeKind.Polyline, Points.Count: >= 3, Locked: false };

    /// <summary>Makes the polyline a closed polygon in place (no undo step): the fill is switched on, in the colour the shape has (the current fill colour of the tools).</summary>
    public static void Close(Shape shape)
    {
        if (shape.Points is { Count: >= 3 } points && CorePointsTouch(points[0], points[^1]))
        {
            // the last point lies on the first one already: it is dropped, the polygon closes by itself
            shape.Points = points.Take(points.Count - 1).ToList();
        }

        shape.Kind = ShapeKind.Polygon;
        shape.FillEnabled = true;
        PathBoundsCalculator.Recompute(shape);
    }

    /// <summary>Closes every selected open polyline as one undo step. Returns how many were closed.</summary>
    public static int CloseSelected(EditorContext context)
    {
        var polylines = context.SelectedShapes.Where(CanClose).ToList();
        if (polylines.Count == 0)
        {
            context.Notify("Select an open polyline with at least three points to close it.");
            return 0;
        }

        var before = polylines.Select(ShapeSnapshot.Capture).ToList();
        foreach (var shape in polylines)
        {
            Close(shape);
        }

        var after = polylines.Select(ShapeSnapshot.Capture).ToList();
        context.UndoManager.Push(UndoableAction.ForPropertyEdit(polylines, before, after));
        context.RequestRedraw();
        return polylines.Count;
    }

    private static bool CorePointsTouch(CorePoint a, CorePoint b) => Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;
}
