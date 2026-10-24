using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>The drawn outline of a shape as measurable geometry: its corners, the middle of its straight edges, and the nearest points
/// to other things. This is what lets dimensions pin to any corner, edge or wall face and measure the gap between two objects.</summary>
public static class OutlineGeometry
{
    /// <summary>Anchor index of the n-th outline corner (see <see cref="ShapeAnchor"/>).</summary>
    public const int CornerBase = 1000;

    /// <summary>Anchor index of the n-th straight-edge middle.</summary>
    public const int MidBase = 2000;

    /// <summary>Shapes that have no outline worth measuring.</summary>
    public static bool IsMeasurable(Shape shape) =>
        shape.Kind is not (ShapeKind.Dimension or ShapeKind.DesignGrid or ShapeKind.Text);

    // The geometry of a shape is rebuilt only when something that shapes it changed (the editor asks for corners, edges and
    // distances on every pointer move and every redraw).
    private sealed class Cache
    {
        public int Key;
        public List<SKPoint>? Corners;
        public List<SKPoint>? Middles;
        public List<List<SKPoint>>? Fine;
        public List<List<SKPoint>>? Coarse;
        public Shape? ClosestOther;
        public int ClosestOtherKey;
        public (SKPoint A, SKPoint B, float Distance)? Closest;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Shape, Cache> Caches = new();

    /// <summary>A number that changes whenever the outline of the shape changes.</summary>
    public static int GeometryKey(Shape s)
    {
        var hash = new HashCode();
        hash.Add(s.Kind);
        hash.Add(s.X);
        hash.Add(s.Y);
        hash.Add(s.Width);
        hash.Add(s.Height);
        hash.Add(s.Rotation);
        hash.Add(s.FlipHorizontal);
        hash.Add(s.FlipVertical);
        hash.Add(s.CornerRadius);
        hash.Add(s.Depth3D);
        hash.Add(s.WallThickness);
        hash.Add(s.WallClosed);
        hash.Add(s.PathRoutingStyle);
        hash.Add(s.LineStartHead);
        hash.Add(s.LineEndHead);
        hash.Add(s.Points is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s.Points));
        hash.Add(s.Points?.Count ?? 0);
        hash.Add(s.CustomPathData is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s.CustomPathData));
        return hash.ToHashCode();
    }

    private static Cache CacheFor(Shape shape)
    {
        var cache = Caches.GetOrCreateValue(shape);
        var key = GeometryKey(shape);
        if (cache.Key != key)
        {
            cache.Key = key;
            cache.Corners = null;
            cache.Middles = null;
            cache.Fine = null;
            cache.Coarse = null;
            cache.ClosestOther = null;
            cache.Closest = null;
        }

        return cache;
    }

    private static SKPath? BuildPath(Shape shape)
    {
        if (!IsMeasurable(shape))
        {
            return null;
        }

        try
        {
            return ShapeGeometryFactory.Build(shape);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The ends of every segment of the outline, one entry per corner (the closing point is not repeated).</summary>
    public static List<SKPoint> Corners(Shape shape)
    {
        var cache = CacheFor(shape);
        if (cache.Corners is { } known)
        {
            return known;
        }

        var corners = cache.Corners = new List<SKPoint>();
        using var path = BuildPath(shape);
        if (path is null)
        {
            return corners;
        }

        Walk(path, (_, end, _) => AddUnique(corners, end), start => AddUnique(corners, start));
        if (corners.Count > 1 && Same(corners[0], corners[^1]))
        {
            corners.RemoveAt(corners.Count - 1);
        }

        return corners;
    }

    /// <summary>The middle of every straight edge of the outline.</summary>
    public static List<SKPoint> EdgeMiddles(Shape shape)
    {
        var cache = CacheFor(shape);
        if (cache.Middles is { } known)
        {
            return known;
        }

        var middles = cache.Middles = new List<SKPoint>();
        using var path = BuildPath(shape);
        if (path is null)
        {
            return middles;
        }

        Walk(path, (from, to, isLine) =>
        {
            if (isLine && Distance(from, to) > 2f)
            {
                middles.Add(new SKPoint((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            }
        }, null);
        return middles;
    }

    /// <summary>Position of an outline anchor (<see cref="CornerBase"/> + n or <see cref="MidBase"/> + n), or null when the shape no longer has it.</summary>
    public static SKPoint? Position(Shape shape, int index)
    {
        if (index >= MidBase)
        {
            var middles = EdgeMiddles(shape);
            return index - MidBase < middles.Count ? middles[index - MidBase] : null;
        }

        var corners = Corners(shape);
        return index - CornerBase >= 0 && index - CornerBase < corners.Count ? corners[index - CornerBase] : null;
    }

    /// <summary>The outline as short straight pieces (each contour a list of points), fine enough for measuring.</summary>
    public static List<List<SKPoint>> Sample(Shape shape, float step = 3f, int maxPoints = 400)
    {
        var cache = CacheFor(shape);
        var fine = step <= 3f && maxPoints >= 400;
        if ((fine ? cache.Fine : cache.Coarse) is { } known)
        {
            return known;
        }

        var contours = new List<List<SKPoint>>();
        if (fine)
        {
            cache.Fine = contours;
        }
        else
        {
            cache.Coarse = contours;
        }

        using var path = BuildPath(shape);
        if (path is null)
        {
            return contours;
        }

        using var measure = new SKPathMeasure(path, false);
        do
        {
            var length = measure.Length;
            if (length < 0.5f)
            {
                continue;
            }

            var count = Math.Clamp((int)Math.Ceiling(length / step), 2, maxPoints);
            var points = new List<SKPoint>(count + 1);
            for (var i = 0; i <= count; i++)
            {
                if (measure.GetPosition(length * i / count, out var p))
                {
                    points.Add(p);
                }
            }

            if (points.Count > 1)
            {
                contours.Add(points);
            }
        }
        while (measure.NextContour());

        return contours;
    }

    /// <summary>The closest place on the outline to <paramref name="p"/> and its distance.</summary>
    public static (SKPoint Point, float Distance)? NearestOnOutline(Shape shape, SKPoint p)
    {
        SKPoint? best = null;
        var bestDistance = float.MaxValue;
        foreach (var contour in Sample(shape))
        {
            for (var i = 0; i + 1 < contour.Count; i++)
            {
                var q = ClosestOnSegment(contour[i], contour[i + 1], p);
                var d = Distance(q, p);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = q;
                }
            }
        }

        return best is { } found ? (found, bestDistance) : null;
    }

    /// <summary>The two points, one on each outline, that are closest to each other (the gap between two objects). The answer is kept
    /// until one of the two shapes changes.</summary>
    public static (SKPoint A, SKPoint B, float Distance)? ClosestPoints(Shape a, Shape b)
    {
        var cacheA = CacheFor(a);
        var keyB = GeometryKey(b);
        if (ReferenceEquals(cacheA.ClosestOther, b) && cacheA.ClosestOtherKey == keyB)
        {
            return cacheA.Closest;
        }

        var result = ComputeClosest(a, b);
        cacheA.ClosestOther = b;
        cacheA.ClosestOtherKey = keyB;
        cacheA.Closest = result;
        return result;
    }

    private static (SKPoint A, SKPoint B, float Distance)? ComputeClosest(Shape a, Shape b)
    {
        var contoursA = Sample(a, step: 4f, maxPoints: 160);
        var contoursB = Sample(b, step: 4f, maxPoints: 160);
        if (contoursA.Count == 0 || contoursB.Count == 0)
        {
            return null;
        }

        SKPoint bestA = default;
        SKPoint bestB = default;
        var best = float.MaxValue;

        foreach (var ca in contoursA)
        {
            var boxA = Bounds(ca);
            foreach (var cb in contoursB)
            {
                // whole outlines (or contours) further apart than the best pair found so far cannot improve it
                if (BoxDistance(boxA, Bounds(cb)) >= best)
                {
                    continue;
                }

                for (var i = 0; i + 1 < ca.Count; i++)
                {
                    var a0 = ca[i];
                    var a1 = ca[i + 1];
                    for (var j = 0; j + 1 < cb.Count; j++)
                    {
                        var (pa, pb, d) = ClosestBetweenSegments(a0, a1, cb[j], cb[j + 1]);
                        if (d < best)
                        {
                            best = d;
                            bestA = pa;
                            bestB = pb;
                        }
                    }
                }
            }
        }

        return best < float.MaxValue ? (bestA, bestB, best) : null;
    }

    /// <summary>True when (x,y) is on the shape: inside its closed outline, or within <paramref name="tolerance"/> of its edge.</summary>
    public static bool HitTest(Shape shape, double x, double y, double tolerance)
    {
        // cheap rejection first: nowhere near the shape's frame
        var frame = ShapeOutline.Box(shape);
        var reach = tolerance + (shape.Kind == ShapeKind.Wall ? shape.WallThickness : 0) + 2;
        if (x < frame.X - reach || x > frame.X + frame.W + reach || y < frame.Y - reach || y > frame.Y + frame.H + reach)
        {
            return false;
        }

        using var path = BuildPath(shape);
        if (path is null)
        {
            return false;
        }

        if (shape.Kind != ShapeKind.Wall && path.Contains((float)x, (float)y))
        {
            return true;
        }

        return NearestOnOutline(shape, new SKPoint((float)x, (float)y)) is { } near && near.Distance <= tolerance;
    }

    private static SKRect Bounds(List<SKPoint> points)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        foreach (var p in points)
        {
            minX = MathF.Min(minX, p.X);
            minY = MathF.Min(minY, p.Y);
            maxX = MathF.Max(maxX, p.X);
            maxY = MathF.Max(maxY, p.Y);
        }

        return new SKRect(minX, minY, maxX, maxY);
    }

    private static float BoxDistance(SKRect a, SKRect b)
    {
        var dx = MathF.Max(0, MathF.Max(a.Left - b.Right, b.Left - a.Right));
        var dy = MathF.Max(0, MathF.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // ----- helpers ---------------------------------------------------------------------------------------

    /// <summary>Calls <paramref name="segment"/> for every segment (from, to, isStraight) of the path and <paramref name="contourStart"/>
    /// for the first point of every contour.</summary>
    private static void Walk(SKPath path, Action<SKPoint, SKPoint, bool> segment, Action<SKPoint>? contourStart)
    {
        using var iterator = path.CreateIterator(false);
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    contourStart?.Invoke(points[0]);
                    break;
                case SKPathVerb.Line:
                    segment(points[0], points[1], true);
                    break;
                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                    segment(points[0], points[2], false);
                    break;
                case SKPathVerb.Cubic:
                    segment(points[0], points[3], false);
                    break;
                case SKPathVerb.Close:
                    break;
            }
        }

    }

    private static void AddUnique(List<SKPoint> list, SKPoint p)
    {
        if (list.Count == 0 || !Same(list[^1], p))
        {
            list.Add(p);
        }
    }

    private static bool Same(SKPoint a, SKPoint b) => Math.Abs(a.X - b.X) < 0.01f && Math.Abs(a.Y - b.Y) < 0.01f;

    private static float Distance(SKPoint a, SKPoint b) => Geo.Distance(a, b);

    private static SKPoint ClosestOnSegment(SKPoint a, SKPoint b, SKPoint p) => Geo.ClosestOnSegment(a, b, p);

    /// <summary>Closest points of two segments (the standard clamped solution; crossing segments give distance 0).</summary>
    private static (SKPoint, SKPoint, float) ClosestBetweenSegments(SKPoint p1, SKPoint q1, SKPoint p2, SKPoint q2)
    {
        const float epsilon = 1e-9f;
        var d1 = new SKPoint(q1.X - p1.X, q1.Y - p1.Y);
        var d2 = new SKPoint(q2.X - p2.X, q2.Y - p2.Y);
        var r = new SKPoint(p1.X - p2.X, p1.Y - p2.Y);
        var a = d1.X * d1.X + d1.Y * d1.Y;
        var e = d2.X * d2.X + d2.Y * d2.Y;
        var f = d2.X * r.X + d2.Y * r.Y;
        float s;
        float t;

        if (a <= epsilon && e <= epsilon)
        {
            return (p1, p2, Distance(p1, p2));
        }

        if (a <= epsilon)
        {
            s = 0;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            var c = d1.X * r.X + d1.Y * r.Y;
            if (e <= epsilon)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                var b = d1.X * d2.X + d1.Y * d2.Y;
                var denominator = a * e - b * b;
                s = denominator > epsilon ? Math.Clamp((b * f - c * e) / denominator, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }

        var c1 = new SKPoint(p1.X + d1.X * s, p1.Y + d1.Y * s);
        var c2 = new SKPoint(p2.X + d2.X * t, p2.Y + d2.Y * t);
        return (c1, c2, Distance(c1, c2));
    }
}
