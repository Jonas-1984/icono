using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>The Add Point and Delete Point tools, shared by the Windows program and the tablet app: a point is put into the side of a line, wall or
/// shape of points nearest to a place, or the point nearest to a place is taken out. One undo step each.</summary>
public static class PointEditing
{
    private static List<(double X, double Y)> Absolute(Shape shape) =>
        shape.Points!.Select(p => (shape.X + p.X, shape.Y + p.Y)).ToList();

    private static bool IsClosed(Shape shape) => shape.Kind == ShapeKind.Wall ? shape.WallClosed : CornerFillet.IsClosed(shape) && shape.Points!.Count >= 3;

    /// <summary>The side (its first point) nearest to the place and where the new point goes (on that side), or null when none is within
    /// <paramref name="tolerance"/>.</summary>
    public static (int After, double X, double Y)? NearestSide(Shape shape, double x, double y, double tolerance)
    {
        if (shape.Points is not { Count: >= 2 })
        {
            return null;
        }

        var points = Absolute(shape);
        var sides = IsClosed(shape) ? points.Count : points.Count - 1;
        (int After, double X, double Y, double Distance)? best = null;
        for (var i = 0; i < sides; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared < 1e-9 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared, 0, 1);
            double px = a.X + t * dx, py = a.Y + t * dy;
            var distance = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            if (distance <= tolerance && (best is null || distance < best.Value.Distance))
            {
                best = (i, px, py, distance);
            }
        }

        return best is { } found ? (found.After, found.X, found.Y) : null;
    }

    /// <summary>Puts a point into the side nearest to the place. False when no side is near or the shape is locked.</summary>
    public static bool InsertPointNear(EditorContext context, Shape shape, double x, double y, double tolerance)
    {
        if (shape.Locked || NearestSide(shape, x, y, tolerance) is not { } side)
        {
            return false;
        }

        var before = ShapeSnapshot.Capture(shape);
        var list = shape.Points!.ToList();
        list.Insert(side.After + 1, new CorePoint(side.X - shape.X, side.Y - shape.Y));
        shape.PointRadii = CornerFillet.AfterInsert(shape.PointRadii, list.Count - 1, side.After);
        shape.Points = list;
        if (shape.Kind == ShapeKind.Wall)
        {
            WallEditor.RemapOverridesAfterInsert(shape, side.After);
        }

        PathBoundsCalculator.Recompute(shape);
        context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [ShapeSnapshot.Capture(shape)]));
        context.RequestRedraw();
        return true;
    }

    /// <summary>Takes out the point nearest to the place (a shape keeps at least two). False when none is near, the shape would have fewer than two
    /// points, or it is locked.</summary>
    public static bool RemovePointNear(EditorContext context, Shape shape, double x, double y, double tolerance)
    {
        if (shape.Locked || shape.Points is not { Count: > 2 })
        {
            return false;
        }

        var points = Absolute(shape);
        var nearest = -1;
        var bestDistance = tolerance;
        for (var i = 0; i < points.Count; i++)
        {
            var distance = Math.Sqrt((x - points[i].X) * (x - points[i].X) + (y - points[i].Y) * (y - points[i].Y));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                nearest = i;
            }
        }

        if (nearest < 0)
        {
            return false;
        }

        var before = ShapeSnapshot.Capture(shape);
        var list = shape.Points.ToList();
        if (shape.Kind == ShapeKind.Wall)
        {
            WallEditor.RemapOverridesBeforeRemove(shape, nearest);
        }

        shape.PointRadii = CornerFillet.AfterRemove(shape.PointRadii, list.Count, nearest);
        list.RemoveAt(nearest);
        shape.Points = list;
        PathBoundsCalculator.Recompute(shape);
        context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [ShapeSnapshot.Capture(shape)]));
        context.RequestRedraw();
        return true;
    }
}
