using System.Runtime.CompilerServices;
using Icono.Core;

namespace Icono.Rendering;

/// <summary>
/// Connection points: every ordinary shape offers 16 spots on its bounding box (the four corners and three points on each
/// side) where a line or arrow can be glued. Gluing is stored on the line (<see cref="Shape.StartAnchor"/> /
/// <see cref="Shape.EndAnchor"/>); <see cref="Refresh"/> keeps glued ends on their point when the target shape is moved,
/// resized or rotated.
/// </summary>
public static class ConnectionRouter
{
    public const int PointCount = 16;

    /// <summary>Two positions closer than this (in px) count as "the same place".</summary>
    private const double SameSpot = 0.5;

    // Walks the box clockwise from the top-left corner: top side, right side, bottom side, left side.
    private static readonly (double Fx, double Fy)[] Layout =
    [
        (0, 0), (0.25, 0), (0.5, 0), (0.75, 0),
        (1, 0), (1, 0.25), (1, 0.5), (1, 0.75),
        (1, 1), (0.75, 1), (0.5, 1), (0.25, 1),
        (0, 1), (0, 0.75), (0, 0.5), (0, 0.25),
    ];

    /// <summary>Last position each glued end was put on by this class — tells "the target moved" apart from "the user moved the line".</summary>
    private sealed class Memory
    {
        public (double X, double Y)? Start;
        public (double X, double Y)? End;
    }

    private static readonly ConditionalWeakTable<Shape, Memory> Remembered = new();

    /// <summary>Lines (and other point-lists) are the things being glued, never the things glued to.</summary>
    public static bool CanBeTarget(Shape shape) => shape.Points is null && !ShapeGeometryFactory.IsLineFamily(shape.Kind);

    private sealed class BoxCache
    {
        public (ShapeKind, double, double, double, double, double, bool, bool, double, string?) Key;
        public (double Left, double Top, double Width, double Height) Box;
    }

    private static readonly ConditionalWeakTable<Shape, BoxCache> Boxes = new();

    /// <summary>The box the points sit on: the drawn outline of the shape (the icon templates keep a margin inside their
    /// X/Y/Width/Height frame, and a line must touch what you see), unrotated. Cached until the shape changes.</summary>
    public static (double Left, double Top, double Width, double Height) GetBox(Shape shape)
    {
        var key = (shape.Kind, shape.X, shape.Y, shape.Width, shape.Height, shape.CornerRadius, shape.FlipHorizontal, shape.FlipVertical, shape.Depth3D, shape.CustomPathData);
        var cache = Boxes.GetOrCreateValue(shape);
        if (cache.Key == key && cache.Box.Width > 0)
        {
            return cache.Box;
        }

        var frame = (Left: shape.X, Top: shape.Y, shape.Width, shape.Height);
        try
        {
            var unrotated = new Shape { Kind = shape.Kind };
            ShapeSnapshot.Capture(shape).ApplyTo(unrotated);
            unrotated.Rotation = 0;
            using var path = ShapeGeometryFactory.Build(unrotated);
            var b = path.TightBounds;
            if (b.Width > 0.5f && b.Height > 0.5f)
            {
                frame = (b.Left, b.Top, b.Width, b.Height);
            }
        }
        catch (Exception)
        {
            // keep the plain frame
        }

        cache.Key = key;
        cache.Box = frame;
        return frame;
    }

    /// <summary>Position of connection point <paramref name="index"/> in document space (follows the shape's rotation).</summary>
    public static (double X, double Y) GetPoint(Shape shape, int index)
    {
        var (fx, fy) = Layout[Math.Clamp(index, 0, PointCount - 1)];
        var box = GetBox(shape);
        var x = box.Left + fx * box.Width;
        var y = box.Top + fy * box.Height;

        if (shape.Rotation != 0)
        {
            var cx = shape.X + shape.Width / 2;
            var cy = shape.Y + shape.Height / 2;
            var rad = shape.Rotation * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var dx = x - cx;
            var dy = y - cy;
            x = cx + dx * cos - dy * sin;
            y = cy + dx * sin + dy * cos;
        }

        return (x, y);
    }

    /// <summary>The visible shapes a line could be glued to.</summary>
    public static IEnumerable<Shape> Targets(Document document, Shape? exclude = null) =>
        document.Layers.Where(l => l.Visible).SelectMany(l => l.Shapes).Where(s => !ReferenceEquals(s, exclude) && CanBeTarget(s));

    /// <summary>Nearest connection point to <paramref name="x"/>,<paramref name="y"/> within <paramref name="tolerance"/>, if any.</summary>
    public static (Shape Shape, int Index, double X, double Y)? FindNearest(
        Document document, double x, double y, double tolerance, Shape? exclude = null)
    {
        (Shape Shape, int Index, double X, double Y)? best = null;
        var bestDistance = tolerance;

        foreach (var shape in Targets(document, exclude))
        {
            for (var i = 0; i < PointCount; i++)
            {
                var (px, py) = GetPoint(shape, i);
                var distance = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = (shape, i, px, py);
                }
            }
        }

        return best;
    }

    /// <summary>After a line was placed or an end dragged: glue each end that sits exactly on a connection point, free the others.</summary>
    public static void AttachEnds(Document document, Shape line)
    {
        if (line.Kind == ShapeKind.Dimension)
        {
            return; // dimensions have their own attachment (DimensionRouter)
        }

        if (line.Points is not { Count: >= 2 } points)
        {
            return;
        }

        var memory = Remembered.GetOrCreateValue(line);
        line.StartAnchor = AnchorAt(document, line, points[0], memory, isStart: true);
        line.EndAnchor = AnchorAt(document, line, points[^1], memory, isStart: false);
    }

    private static ShapeAnchor? AnchorAt(Document document, Shape line, CorePoint local, Memory memory, bool isStart)
    {
        var x = line.X + local.X;
        var y = line.Y + local.Y;
        var hit = FindNearest(document, x, y, SameSpot, line);

        if (isStart)
        {
            memory.Start = hit is null ? null : (hit.Value.X, hit.Value.Y);
        }
        else
        {
            memory.End = hit is null ? null : (hit.Value.X, hit.Value.Y);
        }

        return hit is null ? null : new ShapeAnchor(hit.Value.Shape.Id, hit.Value.Index);
    }

    /// <summary>Moves every glued line end to its target's connection point. Cheap enough to run on every redraw.
    /// A glued end whose target is gone, or whose line was moved by hand, is set free.</summary>
    public static void Refresh(Document document)
    {
        Dictionary<Guid, Shape>? byId = null;

        foreach (var layer in document.Layers)
        {
            foreach (var line in layer.Shapes)
            {
                if ((line.StartAnchor is null && line.EndAnchor is null) || line.Points is not { Count: >= 2 } points)
                {
                    continue;
                }

                byId ??= document.Layers.SelectMany(l => l.Shapes).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
                var memory = Remembered.GetOrCreateValue(line);

                var newStart = ResolveEnd(line, points[0], line.StartAnchor, memory.Start, byId, out var startAnchor, out var startRemembered);
                var newEnd = ResolveEnd(line, points[^1], line.EndAnchor, memory.End, byId, out var endAnchor, out var endRemembered);
                memory.Start = startRemembered;
                memory.End = endRemembered;

                if (line.StartAnchor != startAnchor)
                {
                    line.StartAnchor = startAnchor;
                }

                if (line.EndAnchor != endAnchor)
                {
                    line.EndAnchor = endAnchor;
                }

                if (newStart is null && newEnd is null)
                {
                    continue;
                }

                var moved = points.ToArray();
                if (newStart is { } s)
                {
                    moved[0] = new CorePoint(s.X - line.X, s.Y - line.Y);
                }

                if (newEnd is { } e)
                {
                    moved[^1] = new CorePoint(e.X - line.X, e.Y - line.Y);
                }

                line.Points = moved;
                PathBoundsCalculator.Recompute(line);
            }
        }
    }

    /// <returns>The new absolute position of the end when it has to move, otherwise null.</returns>
    private static (double X, double Y)? ResolveEnd(
        Shape line, CorePoint local, ShapeAnchor? anchor, (double X, double Y)? remembered, Dictionary<Guid, Shape> byId,
        out ShapeAnchor? keptAnchor, out (double X, double Y)? keptMemory)
    {
        keptAnchor = anchor;
        keptMemory = remembered;

        if (anchor is not { } glued)
        {
            keptMemory = null;
            return null;
        }

        if (!byId.TryGetValue(glued.ShapeId, out var target) || !CanBeTarget(target))
        {
            keptAnchor = null;
            keptMemory = null;
            return null;
        }

        var current = (X: line.X + local.X, Y: line.Y + local.Y);
        var wanted = GetPoint(target, glued.Index);

        if (Near(current, wanted))
        {
            keptMemory = wanted;
            return null;
        }

        if (remembered is { } last && Near(current, last))
        {
            // The end still sits where we left it, so it is the target that moved: follow it.
            keptMemory = wanted;
            return wanted;
        }

        // The end is somewhere else and was not put there by us: the user moved or edited the line, so let go.
        keptAnchor = null;
        keptMemory = null;
        return null;
    }

    private static bool Near((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < SameSpot && Math.Abs(a.Y - b.Y) < SameSpot;
}
