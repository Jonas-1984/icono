using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>The length of a line or loop arrow as it is drawn, and making it a given length: a line (or polyline) is stretched from its first point,
/// a loop arrow grows or shrinks about its centre. Used by the quick editor next to a selected line.</summary>
public static class LineMetrics
{
    /// <summary>The length of the drawn line in page px (its path, without the line ends). 0 when there is nothing to measure.</summary>
    public static double Length(Shape shape)
    {
        using var path = ShapeGeometryFactory.Build(shape);
        using var measure = new SKPathMeasure(path, false);
        return measure.Length;
    }

    /// <summary>Makes the line <paramref name="targetPx"/> long. A line made of points keeps its first point and its form (it is scaled from there);
    /// a loop arrow keeps its centre. False when the shape cannot be sized this way or the length is not usable.</summary>
    public static bool SetLength(Shape shape, double targetPx)
    {
        if (targetPx < 0.01 || !ShapeGeometryFactory.HasLineEnds(shape.Kind) || (shape.Points is null && !ShapeGeometryFactory.IsLoopArrow(shape.Kind)))
        {
            return false;
        }

        // a rounded corner keeps its radius, so the length is not exactly proportional: a few passes settle it
        for (var pass = 0; pass < 5; pass++)
        {
            var current = Length(shape);
            if (current < 0.01)
            {
                return false;
            }

            if (Math.Abs(current - targetPx) < 0.005)
            {
                break;
            }

            Scale(shape, targetPx / current);
        }

        return true;
    }

    private static void Scale(Shape shape, double factor)
    {
        if (shape.Points is { Count: > 0 } points)
        {
            var origin = points[0];
            shape.Points = points.Select(p => new CorePoint(origin.X + (p.X - origin.X) * factor, origin.Y + (p.Y - origin.Y) * factor)).ToList();
            PathBoundsCalculator.Recompute(shape);
            return;
        }

        var centreX = shape.X + shape.Width / 2;
        var centreY = shape.Y + shape.Height / 2;
        shape.Width *= factor;
        shape.Height *= factor;
        shape.X = centreX - shape.Width / 2;
        shape.Y = centreY - shape.Height / 2;
    }
}
