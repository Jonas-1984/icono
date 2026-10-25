using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>The kinds of point the snapping can find on the objects of the drawing.</summary>
public enum PointSnapKind
{
    /// <summary>A corner or an end of a line, curve or outline.</summary>
    Endpoint,

    /// <summary>The middle of a straight line.</summary>
    Midpoint,

    /// <summary>The centre of a circle or an ellipse.</summary>
    Center,

    /// <summary>The centre of area of a closed shape.</summary>
    GeometricCenter,

    /// <summary>A definition point: the points of a dimension, the corners of a wall's centre line.</summary>
    Node,

    /// <summary>The points of a circle or ellipse at 0, 90, 180 and 270 degrees.</summary>
    Quadrant,

    /// <summary>Where two lines or curves cross.</summary>
    Intersection,

    /// <summary>On the imagined continuation of an open line beyond its end.</summary>
    Extension,

    /// <summary>The insertion point of a text, an image, a door or a piece of furniture.</summary>
    Insertion,

    /// <summary>The foot of the line at a right angle from the last point to an object.</summary>
    Perpendicular,

    /// <summary>The point of a circle or curve where a line from the last point just touches it.</summary>
    Tangent,

    /// <summary>The nearest point on an object.</summary>
    Nearest,

    /// <summary>Where the imagined continuations of two lines cross (they need not touch).</summary>
    ApparentIntersection,

    /// <summary>On the line from the last point that runs parallel to another line.</summary>
    Parallel,
}

/// <summary>Which kinds of point are snapped to. The same settings serve every tool and every object.</summary>
public sealed class PointSnapOptions : ObservableObject
{
    private bool _enabled = true;
    private readonly HashSet<PointSnapKind> _on =
    [
        PointSnapKind.Endpoint, PointSnapKind.Midpoint, PointSnapKind.Center, PointSnapKind.Intersection, PointSnapKind.Extension,
        PointSnapKind.Perpendicular, PointSnapKind.Quadrant, PointSnapKind.GeometricCenter, PointSnapKind.Node, PointSnapKind.Insertion,
    ];

    /// <summary>The master switch. Alt switches snapping off for as long as it is held.</summary>
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }

    public bool IsOn(PointSnapKind kind) => _on.Contains(kind);

    public void Set(PointSnapKind kind, bool on)
    {
        if (on ? _on.Add(kind) : _on.Remove(kind))
        {
            RaisePropertyChanged(kind.ToString());
        }
    }

    /// <summary>The kinds that are on (for saving).</summary>
    public IReadOnlyCollection<PointSnapKind> Active => _on;
}

/// <summary>A line to draw for a snap: the extension of a line, the path of a perpendicular or a parallel.</summary>
public sealed record PointSnapGuide(SKPoint From, SKPoint To);

public sealed class PointSnapResult
{
    public SKPoint Point { get; init; }

    public PointSnapKind Kind { get; init; }

    public List<PointSnapGuide> Guides { get; } = [];

    /// <summary>The name shown next to the snap mark.</summary>
    public string Label => Describe(Kind);

    public static string Describe(PointSnapKind kind) => kind switch
    {
        PointSnapKind.Endpoint => "Endpoint",
        PointSnapKind.Midpoint => "Midpoint",
        PointSnapKind.Center => "Center",
        PointSnapKind.GeometricCenter => "Geometric center",
        PointSnapKind.Node => "Node",
        PointSnapKind.Quadrant => "Quadrant",
        PointSnapKind.Intersection => "Intersection",
        PointSnapKind.Extension => "Extension",
        PointSnapKind.Insertion => "Insertion",
        PointSnapKind.Perpendicular => "Perpendicular",
        PointSnapKind.Tangent => "Tangent",
        PointSnapKind.Nearest => "Nearest",
        PointSnapKind.ApparentIntersection => "Apparent intersection",
        _ => "Parallel",
    };
}

/// <summary>
/// The snapping of points for every tool: a point that is placed, a corner that is dragged, a selection that is moved lands exactly on an end
/// point, middle, centre, crossing ... of the objects around it. It works on the outline of any shape, wall or symbol.
/// </summary>
public static class PointSnapper
{
    /// <summary>How near (document px) things may be for the kinds that need a second object: crossings, apparent crossings, parallels.</summary>
    private const float ReachForPairs = 160f;

    private const float ReachForExtension = 2500f;

    /// <summary>Objects this near (to the pointer) give the direction of a parallel.</summary>
    private const float ReachForParallel = 500f;

    /// <summary>Preference when two snaps are about equally near: the point that says most wins (px of distance it is worth).</summary>
    private static float Bias(PointSnapKind kind) => kind switch
    {
        PointSnapKind.Endpoint or PointSnapKind.Intersection => 0f,
        PointSnapKind.Center or PointSnapKind.GeometricCenter or PointSnapKind.Node or PointSnapKind.Insertion or PointSnapKind.Quadrant => 0.2f,
        PointSnapKind.Midpoint => 0.4f,
        PointSnapKind.Perpendicular or PointSnapKind.Tangent => 0.6f,
        PointSnapKind.Extension => 1f,
        PointSnapKind.ApparentIntersection => 1.2f,
        PointSnapKind.Parallel => 1.4f,
        _ => 3f,
    };

    private readonly record struct Candidate(SKPoint Point, PointSnapKind Kind, SKPoint? GuideFrom);

    /// <param name="cursor">The pointer in document space.</param>
    /// <param name="tolerance">How near (document px) a point has to be.</param>
    /// <param name="exclude">Shapes that must not attract the point (the one being drawn or dragged).</param>
    /// <param name="reference">The last point of what is being drawn: needed for perpendicular, tangent, parallel.</param>
    public static PointSnapResult? Snap(
        Document document, SKPoint cursor, double tolerance, PointSnapOptions options, IReadOnlyCollection<Shape> exclude, SKPoint? reference = null)
    {
        if (!options.Enabled)
        {
            return null;
        }

        var tol = (float)tolerance;
        var candidates = new List<Candidate>();
        var near = new List<(SnapGeometry Geometry, Shape Shape)>();
        var wide = new List<(SnapGeometry Geometry, Shape Shape)>();
        var parallelSources = new List<(SnapGeometry Geometry, Shape Shape)>();

        foreach (var shape in document.VisibleShapes())
        {
            if (exclude.Contains(shape) || shape.Kind == ShapeKind.DesignGrid || !MayBeNear(shape, cursor, ReachForExtension))
            {
                continue;
            }

            var geometry = SnapGeometry.Of(shape);
            var distance = DistanceToBounds(geometry.Bounds, cursor);
            if (distance <= tol + 1)
            {
                near.Add((geometry, shape));
            }

            if (distance <= ReachForPairs)
            {
                wide.Add((geometry, shape));
            }

            if (distance <= ReachForParallel)
            {
                parallelSources.Add((geometry, shape));
            }

            if (options.IsOn(PointSnapKind.Extension))
            {
                AddExtensions(geometry, cursor, tol, candidates);
            }
        }

        foreach (var (geometry, _) in near)
        {
            AddPoints(geometry, cursor, tol, options, candidates);
        }

        var segments = new List<(SKPoint A, SKPoint B, bool Straight)>();
        foreach (var (geometry, _) in near)
        {
            foreach (var contour in geometry.Contours)
            {
                foreach (var segment in contour.Segments())
                {
                    if (DistanceToSegment(cursor, segment.A, segment.B) <= tol + 0.5f)
                    {
                        segments.Add(segment);
                    }
                }
            }
        }

        if (options.IsOn(PointSnapKind.Nearest))
        {
            foreach (var (a, b, _) in segments)
            {
                candidates.Add(new Candidate(Geo.ClosestOnSegment(a, b, cursor), PointSnapKind.Nearest, null));
            }
        }

        if (options.IsOn(PointSnapKind.Intersection))
        {
            AddIntersections(segments, cursor, tol, candidates);
        }

        if (reference is { } from)
        {
            if (options.IsOn(PointSnapKind.Perpendicular))
            {
                foreach (var (a, b, _) in segments)
                {
                    var foot = Geo.ClosestOnSegment(a, b, from);
                    if (Geo.Distance(foot, cursor) <= tol && Geo.Distance(foot, from) > 1f)
                    {
                        candidates.Add(new Candidate(foot, PointSnapKind.Perpendicular, from));
                    }
                }
            }

            if (options.IsOn(PointSnapKind.Tangent))
            {
                foreach (var (geometry, _) in near)
                {
                    AddTangents(geometry, from, cursor, tol, candidates);
                }
            }

            if (options.IsOn(PointSnapKind.Parallel))
            {
                AddParallels(parallelSources, from, cursor, tol, candidates);
            }
        }

        if (options.IsOn(PointSnapKind.ApparentIntersection))
        {
            AddApparentIntersections(wide, cursor, tol, candidates);
        }

        Candidate? best = null;
        var bestScore = float.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = Geo.Distance(candidate.Point, cursor);
            if (distance > tol)
            {
                continue;
            }

            var score = distance + Bias(candidate.Kind);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best is not { } chosen)
        {
            return null;
        }

        var result = new PointSnapResult { Point = chosen.Point, Kind = chosen.Kind };
        if (chosen.GuideFrom is { } origin)
        {
            result.Guides.Add(new PointSnapGuide(origin, chosen.Point));
        }

        return result;
    }

    /// <summary>The fixed points of the objects in an area (end points, middles, centres, quadrants, nodes, insertion points) that are on: the
    /// places a moved selection can be put on. Crossings and the kinds that depend on a pointer are not included.</summary>
    public static List<(SKPoint Point, PointSnapKind Kind)> FixedPoints(
        Document document, SKRect area, PointSnapOptions options, IReadOnlyCollection<Shape> exclude)
    {
        var result = new List<(SKPoint, PointSnapKind)>();
        if (!options.Enabled)
        {
            return result;
        }

        var candidates = new List<Candidate>();
        foreach (var shape in document.VisibleShapes())
        {
            if (exclude.Contains(shape) || shape.Kind == ShapeKind.DesignGrid || !MayBeNear(shape, area))
            {
                continue;
            }

            var geometry = SnapGeometry.Of(shape);
            if (!geometry.Bounds.IntersectsWith(area))
            {
                continue;
            }

            AddPoints(geometry, new SKPoint(area.MidX, area.MidY), float.MaxValue, options, candidates);
        }

        foreach (var candidate in candidates)
        {
            if (area.Contains(candidate.Point.X, candidate.Point.Y))
            {
                result.Add((candidate.Point, candidate.Kind));
            }
        }

        return result;
    }

    /// <summary>The fixed points of the given shapes themselves (what a moved selection brings with it to snap onto the points of others).</summary>
    public static List<(SKPoint Point, PointSnapKind Kind)> PointsOf(IEnumerable<Shape> shapes, PointSnapOptions options)
    {
        var candidates = new List<Candidate>();
        if (options.Enabled)
        {
            foreach (var shape in shapes)
            {
                AddPoints(SnapGeometry.Of(shape), default, float.MaxValue, options, candidates);
            }
        }

        return candidates.Select(c => (c.Point, c.Kind)).ToList();
    }

    // ----- cheap rejection ---------------------------------------------------------------------------------

    /// <summary>A rough test with the frame of the shape (so shapes far away are not even built): the frame, widened by the stroke / wall
    /// band and, for a turned shape, by its half diagonal.</summary>
    private static bool MayBeNear(Shape shape, SKPoint cursor, float reach) =>
        MayBeNear(shape, new SKRect(cursor.X - reach, cursor.Y - reach, cursor.X + reach, cursor.Y + reach));

    private static bool MayBeNear(Shape shape, SKRect area)
    {
        var margin = (float)Math.Max(Math.Max(shape.WallThickness, shape.BorderThickness), 24);
        var frame = new SKRect(
            (float)(shape.X - margin), (float)(shape.Y - margin),
            (float)(shape.X + shape.Width + margin), (float)(shape.Y + shape.Height + margin));
        if (shape.Rotation != 0 || shape.Kind is ShapeKind.Dimension)
        {
            var radius = (float)Math.Sqrt(shape.Width * shape.Width + shape.Height * shape.Height) / 2 + margin;
            var centre = new SKPoint((float)(shape.X + shape.Width / 2), (float)(shape.Y + shape.Height / 2));
            frame = new SKRect(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius);
        }

        return frame.IntersectsWith(area);
    }

    private static float DistanceToBounds(SKRect bounds, SKPoint p)
    {
        var dx = Math.Max(Math.Max(bounds.Left - p.X, 0), p.X - bounds.Right);
        var dy = Math.Max(Math.Max(bounds.Top - p.Y, 0), p.Y - bounds.Bottom);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b) => Geo.Distance(p, Geo.ClosestOnSegment(a, b, p));

    // ----- the kinds -------------------------------------------------------------------------------------------

    private static void AddPoints(SnapGeometry geometry, SKPoint cursor, float tol, PointSnapOptions options, List<Candidate> candidates)
    {
        void Add(SKPoint point, PointSnapKind kind)
        {
            if (Geo.Distance(point, cursor) <= tol)
            {
                candidates.Add(new Candidate(point, kind, null));
            }
        }

        foreach (var contour in geometry.Contours)
        {
            if (options.IsOn(PointSnapKind.Endpoint) && !contour.CentreLine)
            {
                for (var i = 0; i < contour.Samples.Count; i++)
                {
                    if (contour.Corner[i])
                    {
                        Add(contour.Samples[i], PointSnapKind.Endpoint);
                    }
                }
            }

            if (options.IsOn(PointSnapKind.Midpoint))
            {
                foreach (var (a, b, straight) in contour.Segments())
                {
                    if (straight && Geo.Distance(a, b) >= 4)
                    {
                        Add(new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2), PointSnapKind.Midpoint);
                    }
                }
            }
        }

        if (options.IsOn(PointSnapKind.Center) && geometry.Center is { } centre)
        {
            Add(centre, PointSnapKind.Center);
        }

        if (options.IsOn(PointSnapKind.Quadrant) && geometry.Quadrants is { } quadrants)
        {
            foreach (var quadrant in quadrants)
            {
                Add(quadrant, PointSnapKind.Quadrant);
            }
        }

        if (options.IsOn(PointSnapKind.GeometricCenter) && geometry.Centroid is { } centroid)
        {
            Add(centroid, PointSnapKind.GeometricCenter);
        }

        if (options.IsOn(PointSnapKind.Node))
        {
            foreach (var node in geometry.Nodes)
            {
                Add(node, PointSnapKind.Node);
            }
        }

        if (options.IsOn(PointSnapKind.Insertion) && geometry.Insertion is { } insertion)
        {
            Add(insertion, PointSnapKind.Insertion);
        }
    }

    /// <summary>The imagined continuation of every open line beyond its ends.</summary>
    private static void AddExtensions(SnapGeometry geometry, SKPoint cursor, float tol, List<Candidate> candidates)
    {
        foreach (var contour in geometry.Contours.Where(c => !c.Closed && c.Samples.Count >= 2))
        {
            var last = contour.Samples.Count - 1;
            Ray(contour.Samples[last], contour.Samples[last - 1], contour.Straight[last - 1]);
            Ray(contour.Samples[0], contour.Samples[1], contour.Straight[0]);
        }

        void Ray(SKPoint end, SKPoint before, bool straight)
        {
            var length = Geo.Distance(end, before);
            if (!straight || length < 1e-3f)
            {
                return;
            }

            var dx = (end.X - before.X) / length;
            var dy = (end.Y - before.Y) / length;
            var along = (cursor.X - end.X) * dx + (cursor.Y - end.Y) * dy;
            var across = Math.Abs(-(cursor.X - end.X) * dy + (cursor.Y - end.Y) * dx);
            if (along > 2 * tol && across <= tol)
            {
                candidates.Add(new Candidate(new SKPoint(end.X + dx * along, end.Y + dy * along), PointSnapKind.Extension, end));
            }
        }
    }

    private static void AddIntersections(List<(SKPoint A, SKPoint B, bool Straight)> segments, SKPoint cursor, float tol, List<Candidate> candidates)
    {
        var count = Math.Min(segments.Count, 80);
        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (Cross(segments[i], segments[j], inside: true) is { } point && Geo.Distance(point, cursor) <= tol)
                {
                    candidates.Add(new Candidate(point, PointSnapKind.Intersection, null));
                }
            }
        }
    }

    /// <summary>Where the continuations of two lines meet although the lines themselves do not (or only one of them reaches it).</summary>
    private static void AddApparentIntersections(
        List<(SnapGeometry Geometry, Shape Shape)> wide, SKPoint cursor, float tol, List<Candidate> candidates)
    {
        var straight = new List<(SKPoint A, SKPoint B, bool Straight)>();
        foreach (var (geometry, _) in wide)
        {
            foreach (var contour in geometry.Contours)
            {
                straight.AddRange(contour.Segments().Where(s => s.Straight && Geo.Distance(s.A, s.B) > 1f));
            }
        }

        var count = Math.Min(straight.Count, 60);
        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (Cross(straight[i], straight[j], inside: false) is not { } point || Geo.Distance(point, cursor) > tol)
                {
                    continue;
                }

                // a real crossing is the intersection snap's business
                if (OnSegment(point, straight[i]) && OnSegment(point, straight[j]))
                {
                    continue;
                }

                candidates.Add(new Candidate(point, PointSnapKind.ApparentIntersection, null));
            }
        }
    }

    private static bool OnSegment(SKPoint p, (SKPoint A, SKPoint B, bool Straight) s) => DistanceToSegment(p, s.A, s.B) < 0.5f;

    /// <summary>The crossing of two pieces (<paramref name="inside"/>: only when both really cross) or of the infinite lines through them.</summary>
    private static SKPoint? Cross((SKPoint A, SKPoint B, bool Straight) s, (SKPoint A, SKPoint B, bool Straight) t, bool inside)
    {
        double rx = s.B.X - s.A.X, ry = s.B.Y - s.A.Y;
        double qx = t.B.X - t.A.X, qy = t.B.Y - t.A.Y;
        var denominator = rx * qy - ry * qx;
        if (Math.Abs(denominator) < 1e-6)
        {
            return null; // parallel
        }

        var dx = t.A.X - s.A.X;
        var dy = t.A.Y - s.A.Y;
        var u = (dx * qy - dy * qx) / denominator;
        var v = (dx * ry - dy * rx) / denominator;
        if (inside && (u < 1e-4 || u > 1 - 1e-4 || v < 1e-4 || v > 1 - 1e-4))
        {
            return null; // the pieces meet only at an end: that is an end point, not a crossing
        }

        return new SKPoint((float)(s.A.X + u * rx), (float)(s.A.Y + u * ry));
    }

    /// <summary>Points of a curve where a line from <paramref name="from"/> only touches it.</summary>
    private static void AddTangents(SnapGeometry geometry, SKPoint from, SKPoint cursor, float tol, List<Candidate> candidates)
    {
        foreach (var contour in geometry.Contours)
        {
            var s = contour.Samples;
            var n = s.Count;
            for (var i = 0; i < n; i++)
            {
                var previous = contour.Closed ? (i - 1 + n) % n : i - 1;
                var next = contour.Closed ? (i + 1) % n : i + 1;
                if (previous < 0 || next >= n)
                {
                    continue;
                }

                // only on a curve: the pieces on both sides of the sample are pieces of a curve
                if (contour.Straight[previous] || contour.Straight[i] || Geo.Distance(s[i], cursor) > tol)
                {
                    continue;
                }

                var side1 = (s[i].X - from.X) * (s[i].Y - s[previous].Y) - (s[i].Y - from.Y) * (s[i].X - s[previous].X);
                var side2 = (s[i].X - from.X) * (s[next].Y - s[i].Y) - (s[i].Y - from.Y) * (s[next].X - s[i].X);
                if (side1 * side2 <= 0 && Geo.Distance(s[i], from) > 1f)
                {
                    candidates.Add(new Candidate(s[i], PointSnapKind.Tangent, from));
                }
            }
        }
    }

    /// <summary>On the line from <paramref name="from"/> that runs parallel to a line of a nearby object.</summary>
    private static void AddParallels(
        List<(SnapGeometry Geometry, Shape Shape)> wide, SKPoint from, SKPoint cursor, float tol, List<Candidate> candidates)
    {
        var dxCursor = cursor.X - from.X;
        var dyCursor = cursor.Y - from.Y;
        var length = MathF.Sqrt(dxCursor * dxCursor + dyCursor * dyCursor);
        if (length < 4)
        {
            return;
        }

        foreach (var (geometry, _) in wide)
        {
            foreach (var contour in geometry.Contours)
            {
                foreach (var (a, b, straight) in contour.Segments())
                {
                    var l = Geo.Distance(a, b);
                    if (!straight || l < 4)
                    {
                        continue;
                    }

                    var ux = (b.X - a.X) / l;
                    var uy = (b.Y - a.Y) / l;
                    var along = dxCursor * ux + dyCursor * uy;
                    var across = Math.Abs(-dxCursor * uy + dyCursor * ux);
                    if (Math.Abs(along) > 4 && across <= tol)
                    {
                        var point = new SKPoint(from.X + ux * along, from.Y + uy * along);
                        candidates.Add(new Candidate(point, PointSnapKind.Parallel, from));
                    }
                }
            }
        }
    }
}
