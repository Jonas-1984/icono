using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Which things a shape snaps to while it is moved, placed or resized (unlike the snap to the page grid).</summary>
public sealed class ObjectSnapOptions : ObservableObject
{
    private bool _enabled = true;
    private bool _walls = true;
    private bool _objects = true;

    /// <summary>The master switch. Alt switches snapping off for as long as it is held.</summary>
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }

    /// <summary>The faces and corners of walls: a cabinet can be put right against a wall or into a corner.</summary>
    public bool Walls { get => _walls; set => SetField(ref _walls, value); }

    /// <summary>The edges and centres of the other shapes: lined up, side by side, flush.</summary>
    public bool Objects { get => _objects; set => SetField(ref _objects, value); }
}

public enum ObjectSnapKind
{
    Wall,
    Object,
}

/// <summary>A line to show while snapped: vertical at x = <see cref="Position"/> or horizontal at y = <see cref="Position"/>, from
/// <see cref="From"/> to <see cref="To"/> along the other axis.</summary>
public sealed record ObjectSnapGuide(bool Vertical, double Position, double From, double To, ObjectSnapKind Kind);

/// <summary>How far to move (document px) to snap, and the lines to show.</summary>
public sealed record ObjectSnapResult(double Dx, double Dy, IReadOnlyList<ObjectSnapGuide> Guides)
{
    public static ObjectSnapResult None { get; } = new(0, 0, []);

    public bool Snapped => Guides.Count > 0;
}

/// <summary>
/// Magnetic edges for placing things: the frame being moved snaps to the faces of walls (both sides, inner and outer, so a cabinet sits
/// exactly against a wall or in a corner) and to the edges and centres of other shapes. Only straight horizontal and vertical edges take
/// part; a line only attracts the frame where they come close along their length.
/// </summary>
public static class ObjectSnapper
{
    private readonly record struct Line(bool Vertical, double Position, double From, double To, ObjectSnapKind Kind);

    /// <param name="frame">The frame being moved (a point is a frame of size 0).</param>
    /// <param name="exclude">Shapes that move with the frame and so cannot attract it.</param>
    /// <param name="tolerance">How near (document px) an edge has to be.</param>
    public static ObjectSnapResult Snap(
        Document document, (double X, double Y, double W, double H) frame, IReadOnlyCollection<Shape> exclude, double tolerance, ObjectSnapOptions options)
    {
        if (!options.Enabled || (!options.Walls && !options.Objects))
        {
            return ObjectSnapResult.None;
        }

        var lines = Collect(document, frame, exclude, tolerance, options);
        if (lines.Count == 0)
        {
            return ObjectSnapResult.None;
        }

        double left = frame.X, right = frame.X + frame.W, top = frame.Y, bottom = frame.Y + frame.H;
        var centreX = (left + right) / 2;
        var centreY = (top + bottom) / 2;

        var bestX = Best(lines, vertical: true, [(left, false), (right, false), (centreX, true)], top, bottom, tolerance);
        var bestY = Best(lines, vertical: false, [(top, false), (bottom, false), (centreY, true)], left, right, tolerance);

        var guides = new List<ObjectSnapGuide>();
        double dx = 0, dy = 0;
        if (bestX is { } x)
        {
            dx = x.Delta;
        }

        if (bestY is { } y)
        {
            dy = y.Delta;
        }

        // the guides run along the snapped frame and the line it snapped to
        double nLeft = left + dx, nRight = right + dx, nTop = top + dy, nBottom = bottom + dy;
        if (bestX is { } gx)
        {
            guides.Add(new ObjectSnapGuide(true, gx.Line.Position, Math.Min(nTop, gx.Line.From), Math.Max(nBottom, gx.Line.To), gx.Line.Kind));
        }

        if (bestY is { } gy)
        {
            guides.Add(new ObjectSnapGuide(false, gy.Line.Position, Math.Min(nLeft, gy.Line.From), Math.Max(nRight, gy.Line.To), gy.Line.Kind));
        }

        return new ObjectSnapResult(dx, dy, guides);
    }

    private static (double Delta, Line Line)? Best(
        List<Line> lines, bool vertical, (double Edge, bool IsCentre)[] edges, double spanFrom, double spanTo, double tolerance)
    {
        (double Delta, Line Line)? best = null;
        var bestScore = double.MaxValue;

        foreach (var line in lines)
        {
            // a wall face only attracts where the frame is along it; the edges of other shapes line things up from any distance
            if (line.Vertical != vertical ||
                (line.Kind == ObjectSnapKind.Wall && (line.To < spanFrom - tolerance || line.From > spanTo + tolerance)))
            {
                continue;
            }

            foreach (var (edge, isCentre) in edges)
            {
                // wall faces only attract the edges of a frame (a cabinet is put against a wall, not centred on it)
                if (isCentre && line.Kind == ObjectSnapKind.Wall)
                {
                    continue;
                }

                var delta = line.Position - edge;
                if (Math.Abs(delta) > tolerance)
                {
                    continue;
                }

                // the nearest line wins; at a tie a wall beats another shape
                var score = Math.Abs(delta) + (line.Kind == ObjectSnapKind.Wall ? 0 : 0.01) + (isCentre ? 0.005 : 0);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (delta, line);
                }
            }
        }

        return best;
    }

    private static List<Line> Collect(
        Document document, (double X, double Y, double W, double H) frame, IReadOnlyCollection<Shape> exclude, double tolerance, ObjectSnapOptions options)
    {
        var lines = new List<Line>();
        var reach = tolerance + 1;
        var area = new SKRect(
            (float)(frame.X - reach), (float)(frame.Y - reach), (float)(frame.X + frame.W + reach), (float)(frame.Y + frame.H + reach));

        foreach (var shape in document.VisibleShapes())
        {
            if (exclude.Contains(shape) || shape.Kind is ShapeKind.Dimension or ShapeKind.DesignGrid)
            {
                continue;
            }

            if (shape.Kind == ShapeKind.Wall)
            {
                if (options.Walls && shape.Points is { Count: > 1 })
                {
                    AddWallFaces(shape, area, lines);
                }

                continue;
            }

            if (options.Objects && shape.Points is null)
            {
                AddFrameEdges(shape, lines);
            }
        }

        return lines;
    }

    private static void AddFrameEdges(Shape shape, List<Line> lines)
    {
        var box = ShapeOutline.Box(shape); // the outline as drawn, which is also what the selection frame hugs
        double x1 = box.X, x2 = box.X + box.W, y1 = box.Y, y2 = box.Y + box.H;
        lines.Add(new Line(true, x1, y1, y2, ObjectSnapKind.Object));
        lines.Add(new Line(true, x2, y1, y2, ObjectSnapKind.Object));
        lines.Add(new Line(true, (x1 + x2) / 2, y1, y2, ObjectSnapKind.Object));
        lines.Add(new Line(false, y1, x1, x2, ObjectSnapKind.Object));
        lines.Add(new Line(false, y2, x1, x2, ObjectSnapKind.Object));
        lines.Add(new Line(false, (y1 + y2) / 2, x1, x2, ObjectSnapKind.Object));
    }

    /// <summary>The straight horizontal and vertical edges of the wall's outline (every face, inner and outer) that come near the frame.</summary>
    private static void AddWallFaces(Shape wall, SKRect area, List<Line> lines)
    {
        using var outline = WallGeometry.BuildOutline(wall);
        if (!outline.Bounds.IntersectsWith(area))
        {
            return;
        }

        using var iterator = outline.CreateIterator(true);
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            if (verb != SKPathVerb.Line)
            {
                continue;
            }

            var a = points[0];
            var b = points[1];
            var dx = Math.Abs(b.X - a.X);
            var dy = Math.Abs(b.Y - a.Y);
            if (dx < 0.01f && dy > 1f)
            {
                lines.Add(new Line(true, a.X, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), ObjectSnapKind.Wall));
            }
            else if (dy < 0.01f && dx > 1f)
            {
                lines.Add(new Line(false, a.Y, Math.Min(a.X, b.X), Math.Max(a.X, b.X), ObjectSnapKind.Wall));
            }
        }
    }
}
