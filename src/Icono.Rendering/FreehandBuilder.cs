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

    /// <summary>Takes the tremor out of a stroke: the points are laid at an even distance along the stroke and each one is replaced by the weighted
    /// average of its neighbours (a Gaussian of <paramref name="sigma"/> page px), so a shaking hand draws the line it meant to draw. The two ends stay
    /// exactly where the pen went down and was lifted. Strokes that are short or have no sigma are returned as they are.</summary>
    public static List<SKPoint> Stabilize(IReadOnlyList<SKPoint> points, float sigma)
    {
        if (sigma <= 0 || points.Count < 3)
        {
            return points.ToList();
        }

        var step = Math.Max(sigma / 2, 0.05f);
        var samples = Resample(points, step);
        var count = samples.Count;
        if (count < 3)
        {
            return samples;
        }

        var radius = (int)MathF.Ceiling(3 * sigma / step);
        var weights = new float[2 * radius + 1];
        for (var k = -radius; k <= radius; k++)
        {
            var distance = k * step / sigma;
            weights[k + radius] = MathF.Exp(-0.5f * distance * distance);
        }

        var result = new List<SKPoint>(count) { samples[0] };
        for (var i = 1; i < count - 1; i++)
        {
            float x = 0, y = 0, sum = 0;
            for (var k = -radius; k <= radius; k++)
            {
                var p = At(samples, i + k);
                var w = weights[k + radius];
                x += p.X * w;
                y += p.Y * w;
                sum += w;
            }

            result.Add(new SKPoint(x / sum, y / sum));
        }

        result.Add(samples[^1]);
        return result;
    }

    /// <summary>The point at <paramref name="index"/>, where an index beyond an end is the mirror image of the point on the other side of that end: the
    /// stroke goes on in the same direction there, so averaging does not pull the ends inwards.</summary>
    private static SKPoint At(List<SKPoint> samples, int index)
    {
        var last = samples.Count - 1;
        if (index < 0)
        {
            var mirror = samples[Math.Min(-index, last)];
            return new SKPoint(2 * samples[0].X - mirror.X, 2 * samples[0].Y - mirror.Y);
        }

        if (index > last)
        {
            var mirror = samples[Math.Max(2 * last - index, 0)];
            return new SKPoint(2 * samples[last].X - mirror.X, 2 * samples[last].Y - mirror.Y);
        }

        return samples[index];
    }

    /// <summary>The stroke again with a point every <paramref name="step"/> along it (the last point of the stroke is always kept).</summary>
    private static List<SKPoint> Resample(IReadOnlyList<SKPoint> points, float step)
    {
        var result = new List<SKPoint> { points[0] };
        var travelled = 0f;
        var next = step;
        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var length = Geo.Distance(from, to);
            if (length <= 0)
            {
                continue;
            }

            while (travelled + length >= next)
            {
                var t = (next - travelled) / length;
                result.Add(new SKPoint(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t));
                next += step;
            }

            travelled += length;
        }

        var end = points[^1];
        if (result.Count > 1 && Geo.Distance(result[^1], end) < step * 0.5f)
        {
            result[^1] = end;
        }
        else
        {
            result.Add(end);
        }

        return result;
    }

    /// <summary>The smooth stroke through the points: every point is passed with the direction of the stroke there (from its two neighbours), so no
    /// corner shows however the pen moved, and the curve stays where the points are even when they lie far apart.</summary>
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

        var last = points.Count - 1;
        var directions = new SKPoint[points.Count];
        for (var i = 0; i <= last; i++)
        {
            var before = points[Math.Max(i - 1, 0)];
            var after = points[Math.Min(i + 1, last)];
            var dx = after.X - before.X;
            var dy = after.Y - before.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            directions[i] = length > 0 ? new SKPoint(dx / length, dy / length) : default;
        }

        for (var i = 0; i < last; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var reach = Geo.Distance(from, to) / 3;
            path.CubicTo(
                from.X + directions[i].X * reach, from.Y + directions[i].Y * reach,
                to.X - directions[i + 1].X * reach, to.Y - directions[i + 1].Y * reach,
                to.X, to.Y);
        }

        return path;
    }

    /// <summary>Makes the shape of a finished stroke, or null when there is no stroke (no points). <paramref name="tolerance"/> is the page distance a point
    /// may be off the simplified stroke; <paramref name="smoothing"/> is the page distance over which the tremor of the hand is averaged out
    /// (see <see cref="Stabilize"/>, 0 = the stroke as the pen drew it).</summary>
    public static Shape? CreateShape(IReadOnlyList<SKPoint> raw, ArgbColor color, double width, float tolerance, float smoothing = 0)
    {
        if (raw.Count == 0)
        {
            return null;
        }

        using var smooth = Smooth(Simplify(Stabilize(raw, smoothing), tolerance));
        var bounds = smooth.TightBounds;
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
