using Icono.Core;

namespace Icono.Rendering;

/// <summary>
/// Keeps a Points-based shape's X/Y/Width/Height bounding box in sync with its point list after
/// any add/move/delete. Points are stored shape-local (point (0,0) = the shape's X,Y) so the
/// existing "shape.X += dx; shape.Y += dy" move-gesture code keeps working unmodified — this is
/// what re-anchors the points whenever the bounding box's origin shifts.
/// </summary>
public static class PathBoundsCalculator
{
    public static void Recompute(Shape shape)
    {
        var points = shape.Points;
        if (points is null || points.Count == 0)
        {
            return;
        }

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var p in points)
        {
            var absoluteX = shape.X + p.X;
            var absoluteY = shape.Y + p.Y;
            minX = Math.Min(minX, absoluteX);
            minY = Math.Min(minY, absoluteY);
            maxX = Math.Max(maxX, absoluteX);
            maxY = Math.Max(maxY, absoluteY);
        }

        // the sides that are arcs reach beyond their end points: the frame of the shape holds them too
        if (shape.SegmentBulges is { } bulges && bulges.Count == points.Count && bulges.Any(ArcSegment.IsArc))
        {
            var closed = shape.Kind is not (ShapeKind.Polyline or ShapeKind.Line or ShapeKind.Arrow or ShapeKind.DoubleArrow or ShapeKind.Curve);
            var sides = closed && points.Count >= 3 ? points.Count : points.Count - 1;
            for (var i = 0; i < sides; i++)
            {
                var a = new CorePoint(shape.X + points[i].X, shape.Y + points[i].Y);
                var next = points[(i + 1) % points.Count];
                var b = new CorePoint(shape.X + next.X, shape.Y + next.Y);
                foreach (var extreme in ArcSegment.ExtremePoints(a, b, bulges[i]))
                {
                    minX = Math.Min(minX, extreme.X);
                    minY = Math.Min(minY, extreme.Y);
                    maxX = Math.Max(maxX, extreme.X);
                    maxY = Math.Max(maxY, extreme.Y);
                }
            }
        }

        var newPoints = new CorePoint[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            newPoints[i] = new CorePoint(shape.X + points[i].X - minX, shape.Y + points[i].Y - minY);
        }

        shape.X = minX;
        shape.Y = minY;
        shape.Width = Math.Max(1, maxX - minX);
        shape.Height = Math.Max(1, maxY - minY);
        shape.Points = newPoints;
    }
}
