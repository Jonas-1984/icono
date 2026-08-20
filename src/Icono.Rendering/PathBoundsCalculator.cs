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
