using System.Runtime.CompilerServices;
using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>One run of a shape's outline as straight pieces: the vertices (curves are cut into short pieces) and which of them are real
/// corners of the drawing, so the snapping can tell an end point from a point on a curve.</summary>
public sealed class SnapContour
{
    public List<SKPoint> Samples { get; } = [];

    /// <summary>True where the sample is a vertex of the outline (an end of a line or of a curve), false for the points cutting a curve.</summary>
    public List<bool> Corner { get; } = [];

    /// <summary>For sample <c>i</c>: the piece from <c>i</c> to <c>i + 1</c> is a straight line of the drawing (not a piece of a curve).</summary>
    public List<bool> Straight { get; } = [];

    public bool Closed { get; set; }

    /// <summary>The centre line of a wall (as opposed to the faces of its band): its corners are nodes, not end points.</summary>
    public bool CentreLine { get; init; }

    internal void Add(SKPoint point, bool corner, bool straightFromPrevious)
    {
        if (Samples.Count > 0 && Geo.Distance(Samples[^1], point) < 1e-3f)
        {
            if (corner)
            {
                Corner[^1] = true;
            }

            return;
        }

        if (Samples.Count > 0)
        {
            Straight[^1] = straightFromPrevious;
        }

        Samples.Add(point);
        Corner.Add(corner);
        Straight.Add(true);
    }

    /// <summary>The pieces of the contour; a closed one has the piece back to its start too.</summary>
    public IEnumerable<(SKPoint A, SKPoint B, bool Straight)> Segments()
    {
        for (var i = 0; i + 1 < Samples.Count; i++)
        {
            yield return (Samples[i], Samples[i + 1], Straight[i]);
        }

        if (Closed && Samples.Count >= 2 && Geo.Distance(Samples[^1], Samples[0]) > 1e-3f)
        {
            yield return (Samples[^1], Samples[0], true);
        }
    }
}

/// <summary>What the point snapping can latch on to in one shape: its outline as straight pieces, and its special points (centre, quadrants,
/// middle, insertion point, nodes). Computed from the outline the shape is drawn with, so it is right for every kind of shape.</summary>
public sealed class SnapGeometry
{
    public List<SnapContour> Contours { get; } = [];

    public SKRect Bounds { get; private set; }

    /// <summary>The centre of a circle or ellipse.</summary>
    public SKPoint? Center { get; private set; }

    /// <summary>The four points of a circle or ellipse at 0, 90, 180 and 270 degrees.</summary>
    public SKPoint[]? Quadrants { get; private set; }

    /// <summary>The geometric centre of a closed outline.</summary>
    public SKPoint? Centroid { get; private set; }

    /// <summary>Where a text, image, door or piece of furniture is inserted: the corner of its frame.</summary>
    public SKPoint? Insertion { get; private set; }

    /// <summary>Definition points: the points of a dimension, the corners of a wall's centre line.</summary>
    public List<SKPoint> Nodes { get; } = [];

    private static readonly ConditionalWeakTable<Shape, Entry> Cache = new();

    private sealed class Entry(ShapeSnapshot key, SnapGeometry geometry)
    {
        public ShapeSnapshot Key { get; } = key;

        public SnapGeometry Geometry { get; } = geometry;
    }

    /// <summary>The snap geometry of the shape (kept until the shape changes).</summary>
    public static SnapGeometry Of(Shape shape)
    {
        var key = ShapeSnapshot.Capture(shape);
        if (Cache.TryGetValue(shape, out var entry) && entry.Key.Equals(key))
        {
            return entry.Geometry;
        }

        var geometry = Compute(shape);
        Cache.AddOrUpdate(shape, new Entry(key, geometry));
        return geometry;
    }

    private static readonly HashSet<ShapeKind> RoundKinds = [ShapeKind.Ellipse, ShapeKind.Ring, ShapeKind.RoundTable];

    private static SnapGeometry Compute(Shape shape)
    {
        var geometry = new SnapGeometry();
        if (shape.Kind == ShapeKind.DesignGrid)
        {
            return geometry;
        }

        using (var path = ShapeGeometryFactory.Build(shape))
        {
            geometry.Bounds = path.Bounds;
            geometry.Contours.AddRange(Flatten(path));
        }

        if (shape.Kind == ShapeKind.Wall && shape.Points is { Count: > 1 })
        {
            // a wall can be snapped to on its centre line as well as on its faces: a T-joint ends on the centre line of the other wall
            using var centreLine = WallGeometry.BuildCenterLine(shape);
            if (shape.Rotation != 0)
            {
                centreLine.Transform(SKMatrix.CreateRotationDegrees(
                    (float)shape.Rotation, (float)(shape.X + shape.Width / 2), (float)(shape.Y + shape.Height / 2)));
            }

            foreach (var contour in Flatten(centreLine))
            {
                var marked = new SnapContour { Closed = contour.Closed, CentreLine = true };
                for (var i = 0; i < contour.Samples.Count; i++)
                {
                    marked.Add(contour.Samples[i], contour.Corner[i], i > 0 ? contour.Straight[i - 1] : true);
                }

                geometry.Contours.Add(marked);
            }
        }

        var centre = new SKPoint((float)(shape.X + shape.Width / 2), (float)(shape.Y + shape.Height / 2));
        var rotation = SKMatrix.CreateRotationDegrees((float)shape.Rotation, centre.X, centre.Y);

        if (shape.Points is null && RoundKinds.Contains(shape.Kind))
        {
            // the drawn outline of the unturned shape (which is what the circle really is), turned about the centre of the frame like the shape
            var probe = new Shape { Kind = shape.Kind };
            ShapeSnapshot.Capture(shape).ApplyTo(probe);
            probe.Rotation = 0;
            using var unturned = ShapeGeometryFactory.Build(probe);
            var cx = unturned.Bounds.MidX;
            var cy = unturned.Bounds.MidY;
            var rx = unturned.Bounds.Width / 2;
            var ry = unturned.Bounds.Height / 2;
            geometry.Center = rotation.MapPoint(new SKPoint(cx, cy));
            geometry.Quadrants =
            [
                rotation.MapPoint(new SKPoint(cx + rx, cy)),
                rotation.MapPoint(new SKPoint(cx, cy + ry)),
                rotation.MapPoint(new SKPoint(cx - rx, cy)),
                rotation.MapPoint(new SKPoint(cx, cy - ry)),
            ];
        }

        geometry.Centroid = CentreOfArea(geometry.Contours);

        if (shape.Kind is ShapeKind.Text or ShapeKind.RasterImage || IsSymbolOrFurniture(shape.Kind))
        {
            geometry.Insertion = rotation.MapPoint(new SKPoint((float)shape.X, (float)shape.Y));
        }

        if (shape.Kind is ShapeKind.Dimension or ShapeKind.Wall && shape.Points is not null)
        {
            var points = WallGeometry.AbsolutePoints(shape);
            if (shape.Rotation != 0)
            {
                for (var i = 0; i < points.Count; i++)
                {
                    points[i] = rotation.MapPoint(points[i]);
                }
            }

            geometry.Nodes.AddRange(points);
        }

        return geometry;
    }

    /// <summary>Doors, windows, openings and the furniture of the plan symbols: shapes that are placed at a point rather than drawn.</summary>
    public static bool IsSymbolOrFurniture(ShapeKind kind) =>
        ArchitecturalSymbols.IsSymbol(kind) || PlanSymbols.IsPlanSymbol(kind) || kind is >= ShapeKind.Worktable and <= ShapeKind.Lamp;

    internal static List<SnapContour> Flatten(SKPath path)
    {
        var contours = new List<SnapContour>();
        SnapContour? current = null;
        using var iterator = path.CreateIterator(false);
        var p = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(p)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    current = new SnapContour();
                    current.Add(p[0], true, true);
                    contours.Add(current);
                    break;
                case SKPathVerb.Line:
                    current ??= Start(contours, p[0]);
                    current.Add(p[1], true, true);
                    break;
                case SKPathVerb.Quad:
                    current ??= Start(contours, p[0]);
                    AddCurve(current, p[0], t => Quad(p[0], p[1], p[2], t), p[2], Steps(p[0], p[1], p[2]));
                    break;
                case SKPathVerb.Conic:
                {
                    current ??= Start(contours, p[0]);
                    var weight = iterator.ConicWeight();
                    AddCurve(current, p[0], t => Conic(p[0], p[1], p[2], weight, t), p[2], Steps(p[0], p[1], p[2]));
                    break;
                }

                case SKPathVerb.Cubic:
                    current ??= Start(contours, p[0]);
                    AddCurve(current, p[0], t => Cubic(p[0], p[1], p[2], p[3], t), p[3], Steps(p[0], p[1], p[2], p[3]));
                    break;
                case SKPathVerb.Close:
                    if (current is not null)
                    {
                        current.Closed = true;
                    }

                    break;
            }
        }

        return contours.Where(c => c.Samples.Count >= 2).ToList();
    }

    private static SnapContour Start(List<SnapContour> contours, SKPoint start)
    {
        var contour = new SnapContour();
        contour.Add(start, true, true);
        contours.Add(contour);
        return contour;
    }

    private static int Steps(params SKPoint[] controlPolygon)
    {
        float length = 0;
        for (var i = 1; i < controlPolygon.Length; i++)
        {
            length += Geo.Distance(controlPolygon[i - 1], controlPolygon[i]);
        }

        return Math.Clamp((int)(length / 3), 8, 48);
    }

    private static void AddCurve(SnapContour contour, SKPoint start, Func<float, SKPoint> at, SKPoint end, int steps)
    {
        for (var i = 1; i < steps; i++)
        {
            contour.Add(at(i / (float)steps), false, false);
        }

        contour.Add(end, true, false);
    }

    private static SKPoint Quad(SKPoint a, SKPoint b, SKPoint c, float t)
    {
        var u = 1 - t;
        return new SKPoint(u * u * a.X + 2 * u * t * b.X + t * t * c.X, u * u * a.Y + 2 * u * t * b.Y + t * t * c.Y);
    }

    private static SKPoint Conic(SKPoint a, SKPoint b, SKPoint c, float w, float t)
    {
        var u = 1 - t;
        var denominator = u * u + 2 * w * u * t + t * t;
        return new SKPoint(
            (u * u * a.X + 2 * w * u * t * b.X + t * t * c.X) / denominator,
            (u * u * a.Y + 2 * w * u * t * b.Y + t * t * c.Y) / denominator);
    }

    private static SKPoint Cubic(SKPoint a, SKPoint b, SKPoint c, SKPoint d, float t)
    {
        var u = 1 - t;
        var x = u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X;
        var y = u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y;
        return new SKPoint(x, y);
    }

    /// <summary>The centre of area of the largest closed outline (null when there is none or it has no area).</summary>
    private static SKPoint? CentreOfArea(List<SnapContour> contours)
    {
        SKPoint? best = null;
        double bestArea = 0;
        foreach (var contour in contours.Where(c => c.Closed && c.Samples.Count >= 3))
        {
            double area = 0, cx = 0, cy = 0;
            var s = contour.Samples;
            for (var i = 0; i < s.Count; i++)
            {
                var a = s[i];
                var b = s[(i + 1) % s.Count];
                var cross = (double)a.X * b.Y - (double)b.X * a.Y;
                area += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }

            area /= 2;
            if (Math.Abs(area) > Math.Abs(bestArea) && Math.Abs(area) > 1e-3)
            {
                bestArea = area;
                best = new SKPoint((float)(cx / (6 * area)), (float)(cy / (6 * area)));
            }
        }

        return best;
    }
}
