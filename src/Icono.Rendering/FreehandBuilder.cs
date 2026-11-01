using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>The Handwriting tool: the points a pen, a finger or the mouse went through become one smooth stroke. The stroke is kept as a path
/// (<see cref="Shape.CustomPathData"/>, stroke only) so it can be moved, resized, recoloured and deleted like any other shape.</summary>
public static class FreehandBuilder
{
    /// <summary>Takes out the points that lie on the line between their neighbours (Ramer-Douglas-Peucker): the pen delivers far more points than
    /// the shape of the stroke needs, and fewer points make a smoother, smaller stroke.</summary>
    public static List<SKPoint> Simplify(IReadOnlyList<SKPoint> points, float tolerance)
    {
        if (points.Count < 3)
        {
            return points.ToList();
        }

        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int From, int To)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            var farthest = -1;
            var distance = tolerance;
            for (var i = from + 1; i < to; i++)
            {
                var d = Geo.Distance(points[i], Geo.ClosestOnSegment(points[from], points[to], points[i]));
                if (d > distance)
                {
                    distance = d;
                    farthest = i;
                }
            }

            if (farthest >= 0)
            {
                keep[farthest] = true;
                stack.Push((from, farthest));
                stack.Push((farthest, to));
            }
        }

        return points.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>The smooth stroke through the points: curves that pass the points' middles and use the points themselves as their bends, so no corner
    /// shows however the pen moved.</summary>
    public static SKPath Smooth(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath();
        if (points.Count == 0)
        {
            return path;
        }

        path.MoveTo(points[0]);
        if (points.Count == 1)
        {
            path.LineTo(points[0].X + 0.1f, points[0].Y); // a dot (the round cap makes it a dot)
            return path;
        }

        if (points.Count == 2)
        {
            path.LineTo(points[1]);
            return path;
        }

        for (var i = 1; i < points.Count - 1; i++)
        {
            var middle = new SKPoint((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
            path.QuadTo(points[i], i == points.Count - 2 ? points[^1] : middle);
        }

        return path;
    }

    /// <summary>Makes the shape of a finished stroke, or null when there is no stroke (no points). <paramref name="tolerance"/> is the page distance a point
    /// may be off the simplified stroke.</summary>
    public static Shape? CreateShape(IReadOnlyList<SKPoint> raw, ArgbColor color, double width, float tolerance)
    {
        if (raw.Count == 0)
        {
            return null;
        }

        using var smooth = Smooth(Simplify(raw, tolerance));
        var bounds = smooth.Bounds;
        var frameWidth = Math.Max(1f, bounds.Width);
        var frameHeight = Math.Max(1f, bounds.Height);

        // the path is stored in the 24 x 24 box every path shape is scaled from
        using var local = new SKPath(smooth);
        local.Transform(SKMatrix.CreateScaleTranslation(24f / frameWidth, 24f / frameHeight, -bounds.Left * 24f / frameWidth, -bounds.Top * 24f / frameHeight));

        return new Shape
        {
            Kind = ShapeKind.CombinedPath,
            Name = "Handwriting",
            X = bounds.Left,
            Y = bounds.Top,
            Width = frameWidth,
            Height = frameHeight,
            CustomPathData = local.ToSvgPathData(),
            CustomPathStrokeOnly = true,
            FillEnabled = false,
            Freehand = true,
            BorderColor = color,
            BorderThickness = width,
        };
    }

    /// <summary>True when the point lies on the drawn stroke (within half its width and <paramref name="tolerance"/>): a stroke is picked where it is,
    /// not anywhere in its frame, so notes written over a plan do not catch the clicks meant for the plan.</summary>
    public static bool HitTest(Shape shape, double x, double y, double tolerance) =>
        OutlineGeometry.NearestOnOutline(shape, new SKPoint((float)x, (float)y)) is { } near && near.Distance <= shape.BorderThickness / 2 + tolerance;
}
