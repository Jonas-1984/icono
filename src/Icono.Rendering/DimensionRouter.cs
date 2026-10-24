using System.Runtime.CompilerServices;
using Icono.Core;

namespace Icono.Rendering;

/// <summary>Keeps dimensions on their shape: when the shape moves, resizes or turns, its dimensions follow and stay at the same
/// distance. This covers dimensions created for a shape ("Add dimensions", see <see cref="DimensionRole"/>) and dimensions placed by
/// hand whose points sit on a shape's connection points, corners or centre. A dimension (or point) the user moved by hand, or whose shape
/// is gone, becomes free.</summary>
public static class DimensionRouter
{
    private const double Same = 0.5;

    /// <summary>The centre of a shape's drawn outline, as an anchor index next to the 16 connection points.</summary>
    public const int CenterIndex = 16;

    private sealed class Memory
    {
        public List<(double X, double Y)>? Points;
        public (double X, double Y)?[]? PerPoint;
    }

    private static readonly ConditionalWeakTable<Shape, Memory> Remembered = new();

    // ----- placed by hand: which shape part does each point sit on? ---------------------------------------------

    /// <summary>The place on a shape (connection point, corner or centre) at the given spot, if there is one.</summary>
    public static ShapeAnchor? FindAnchorAt(Document document, double x, double y, Shape? exclude = null)
    {
        foreach (var layer in document.Layers.Where(l => l.Visible))
        {
            foreach (var shape in layer.Shapes)
            {
                if (ReferenceEquals(shape, exclude) || shape.Kind is ShapeKind.Dimension)
                {
                    continue;
                }

                if (shape.Points is { Count: > 0 } points)
                {
                    for (var i = 0; i < points.Count; i++)
                    {
                        if (Near(shape.X + points[i].X, shape.Y + points[i].Y, x, y))
                        {
                            return new ShapeAnchor(shape.Id, i);
                        }
                    }

                    continue;
                }

                for (var i = 0; i <= CenterIndex; i++)
                {
                    var p = AnchorPosition(shape, i);
                    if (Near(p.X, p.Y, x, y))
                    {
                        return new ShapeAnchor(shape.Id, i);
                    }
                }
            }
        }

        return null;
    }

    private static bool Near(double ax, double ay, double bx, double by) => Math.Abs(ax - bx) <= Same && Math.Abs(ay - by) <= Same;

    private static (double X, double Y) AnchorPosition(Shape shape, int index)
    {
        if (shape.Points is { Count: > 0 } points)
        {
            var i = Math.Clamp(index, 0, points.Count - 1);
            return (shape.X + points[i].X, shape.Y + points[i].Y);
        }

        if (index >= CenterIndex)
        {
            var box = ShapeOutline.Box(shape);
            var cx = box.X + box.W / 2;
            var cy = box.Y + box.H / 2;
            if (shape.Rotation != 0)
            {
                var pivotX = shape.X + shape.Width / 2;
                var pivotY = shape.Y + shape.Height / 2;
                var rad = shape.Rotation * Math.PI / 180.0;
                var dx = cx - pivotX;
                var dy = cy - pivotY;
                return (pivotX + dx * Math.Cos(rad) - dy * Math.Sin(rad), pivotY + dx * Math.Sin(rad) + dy * Math.Cos(rad));
            }

            return (cx, cy);
        }

        return ConnectionRouter.GetPoint(shape, index);
    }

    /// <summary>How many leading points of a dimension are measured points (the rest only place the line or the text).</summary>
    private static int MeasuredPoints(DimensionKind kind) => kind switch
    {
        DimensionKind.Angular => 3,
        _ => 2,
    };

    /// <summary>After a dimension was placed by hand: glue every measured point that lies exactly on a shape part, and remember how
    /// far the dimension line sits from them.</summary>
    public static void AttachPoints(Document document, Shape dim)
    {
        if (dim.Kind != ShapeKind.Dimension || dim.Points is not { Count: >= 2 } points || dim.DimTarget is not null)
        {
            return;
        }

        var measured = Math.Min(MeasuredPoints(dim.DimKind), points.Count);
        var anchors = new ShapeAnchor?[points.Count];
        var any = false;
        for (var i = 0; i < measured; i++)
        {
            anchors[i] = FindAnchorAt(document, dim.X + points[i].X, dim.Y + points[i].Y);
            any |= anchors[i] is not null;
        }

        dim.DimAnchors = any ? anchors : null;
        if (!any)
        {
            return;
        }

        var abs = points.Select(p => (X: dim.X + p.X, Y: dim.Y + p.Y)).ToList();
        dim.DimOffset = PlacementOffset(dim.DimKind, abs);
        var memory = Remembered.GetOrCreateValue(dim);
        memory.PerPoint = abs.Select(p => ((double, double)?)p).ToArray();
    }

    /// <summary>Distance of the dimension line from the measured points, signed, in the way each kind needs to rebuild it.</summary>
    private static double PlacementOffset(DimensionKind kind, List<(double X, double Y)> p)
    {
        if (p.Count < 3 && kind != DimensionKind.Radius && kind != DimensionKind.Diameter)
        {
            return 0;
        }

        switch (kind)
        {
            case DimensionKind.Horizontal:
                return p[2].Y - p[0].Y;
            case DimensionKind.Vertical:
                return p[2].X - p[0].X;
            case DimensionKind.Aligned:
            {
                var dx = p[1].X - p[0].X;
                var dy = p[1].Y - p[0].Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                return length < 1e-9 ? 0 : ((p[2].X - p[0].X) * -dy + (p[2].Y - p[0].Y) * dx) / length;
            }

            case DimensionKind.Angular:
                return p.Count > 3 ? Math.Sqrt((p[3].X - p[0].X) * (p[3].X - p[0].X) + (p[3].Y - p[0].Y) * (p[3].Y - p[0].Y)) : 0;
            default:
                return 0;
        }
    }

    // ----- following ------------------------------------------------------------------------------------

    public static void Refresh(Document document)
    {
        Dictionary<Guid, Shape>? byId = null;

        foreach (var layer in document.Layers)
        {
            foreach (var dim in layer.Shapes)
            {
                if (dim.Kind != ShapeKind.Dimension || dim.Points is null)
                {
                    continue;
                }

                var attachedToRole = dim.DimTarget is not null && dim.DimRole != DimensionRole.None;
                var attachedByHand = dim.DimAnchors is { Count: > 0 } && dim.DimTarget is null;
                if (!attachedToRole && !attachedByHand)
                {
                    continue;
                }

                byId ??= document.Layers.SelectMany(l => l.Shapes).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());

                if (attachedToRole)
                {
                    RefreshRole(dim, byId);
                }
                else
                {
                    RefreshByHand(dim, byId);
                }
            }
        }
    }

    private static void RefreshRole(Shape dim, Dictionary<Guid, Shape> byId)
    {
        if (!byId.TryGetValue(dim.DimTarget!.Value, out var target))
        {
            dim.DimTarget = null;
            return;
        }

        var wanted = DimensionBuilder.Compute(target, dim.DimRole, dim.DimIndex, dim.DimOffset, dim.DimAngle);
        if (wanted is null)
        {
            dim.DimTarget = null;
            return;
        }

        var current = dim.Points!.Select(p => (dim.X + p.X, dim.Y + p.Y)).ToList();
        var memory = Remembered.GetOrCreateValue(dim);

        if (Matches(current, wanted.Points))
        {
            memory.Points = wanted.Points;
            if (Math.Abs(dim.DimValue - wanted.Value) > 1e-6)
            {
                dim.DimValue = wanted.Value;
            }

            return;
        }

        if (memory.Points is { } last && Matches(current, last))
        {
            // Still where we left it, so the shape (or an offset) changed: follow.
            DimensionBuilder.Place(dim, wanted);
            memory.Points = wanted.Points;
            return;
        }

        // Moved by the user (or never ours): let go.
        dim.DimTarget = null;
        memory.Points = null;
    }

    private static void RefreshByHand(Shape dim, Dictionary<Guid, Shape> byId)
    {
        var anchors = dim.DimAnchors!;
        var points = dim.Points!;
        var abs = points.Select(p => (X: dim.X + p.X, Y: dim.Y + p.Y)).ToList();
        var memory = Remembered.GetOrCreateValue(dim);
        var last = memory.PerPoint;
        var next = new ShapeAnchor?[anchors.Count];
        var changed = false;
        var moved = abs.ToList();
        var newMemory = new (double X, double Y)?[abs.Count];
        (double X, double Y)? firstShift = null;

        for (var i = 0; i < abs.Count; i++)
        {
            newMemory[i] = last is not null && i < last.Length ? last[i] : null;
            if (i >= anchors.Count || anchors[i] is not { } anchor)
            {
                continue;
            }

            next[i] = anchor;
            if (!byId.TryGetValue(anchor.ShapeId, out var target) || target.Kind == ShapeKind.Dimension)
            {
                next[i] = null;
                continue;
            }

            var wanted = AnchorPosition(target, anchor.Index);
            if (Near(abs[i].X, abs[i].Y, wanted.X, wanted.Y))
            {
                newMemory[i] = wanted;
            }
            else if (last is not null && i < last.Length && last[i] is { } was && Near(abs[i].X, abs[i].Y, was.X, was.Y))
            {
                moved[i] = wanted;
                newMemory[i] = wanted;
                firstShift ??= (wanted.X - abs[i].X, wanted.Y - abs[i].Y);
                changed = true;
            }
            else
            {
                next[i] = null; // moved by hand: this point is free now
                newMemory[i] = null;
            }
        }

        memory.PerPoint = newMemory;

        if (changed)
        {
            RebuildFree(dim, abs, moved, next, byId, firstShift);
            Apply(dim, moved);
            memory.PerPoint = moved.Select((p, i) => i < next.Length && next[i] is not null ? ((double, double)?)p : newMemory[i]).ToArray();
        }

        if (!next.Any(a => a is not null))
        {
            dim.DimAnchors = null;
            return;
        }

        if (next.Select((a, i) => (a, i)).Any(t => t.a != (t.i < anchors.Count ? anchors[t.i] : null)))
        {
            dim.DimAnchors = next;
        }
    }

    /// <summary>Puts the points that are not glued (the dimension line, the arc, the second point of a radius) where they belong
    /// after the glued ones moved: the dimension line keeps its distance, the arc its radius, a circle's edge its place on the circle.</summary>
    private static void RebuildFree(
        Shape dim, List<(double X, double Y)> before, List<(double X, double Y)> moved, ShapeAnchor?[] anchors,
        Dictionary<Guid, Shape> byId, (double X, double Y)? firstShift)
    {
        bool Glued(int i) => i < anchors.Length && anchors[i] is not null;

        switch (dim.DimKind)
        {
            case DimensionKind.Horizontal when moved.Count > 2 && !Glued(2):
                moved[2] = (moved[0].X, moved[0].Y + dim.DimOffset);
                break;
            case DimensionKind.Vertical when moved.Count > 2 && !Glued(2):
                moved[2] = (moved[0].X + dim.DimOffset, moved[0].Y);
                break;
            case DimensionKind.Aligned when moved.Count > 2 && !Glued(2):
            {
                var dx = moved[1].X - moved[0].X;
                var dy = moved[1].Y - moved[0].Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 1e-9)
                {
                    moved[2] = (moved[0].X + -dy / length * dim.DimOffset, moved[0].Y + dx / length * dim.DimOffset);
                }

                break;
            }

            case DimensionKind.Angular when moved.Count > 3 && !Glued(3):
            {
                var v = moved[0];
                var ua = Unit(v, moved[1]);
                var ub = Unit(v, moved[2]);
                var bx = ua.X + ub.X;
                var by = ua.Y + ub.Y;
                var bl = Math.Sqrt(bx * bx + by * by);
                var dir = bl < 1e-6 ? (-ua.Y, ua.X) : (bx / bl, by / bl);
                moved[3] = (v.X + dir.Item1 * dim.DimOffset, v.Y + dir.Item2 * dim.DimOffset);
                break;
            }

            case DimensionKind.Radius or DimensionKind.Diameter when moved.Count > 1 && !Glued(1):
            {
                var center = moved[0];
                var dir = Unit(before[0], before[1]);
                var radius = Math.Sqrt((before[1].X - before[0].X) * (before[1].X - before[0].X) + (before[1].Y - before[0].Y) * (before[1].Y - before[0].Y));
                if (anchors[0] is { } a && byId.TryGetValue(a.ShapeId, out var target) && target.Points is null && a.Index == CenterIndex)
                {
                    var box = ShapeOutline.Box(target);
                    radius = Math.Max(box.W, box.H) / 2; // a circle keeps its edge on the circle when it is resized
                }

                moved[1] = (center.X + dir.X * radius, center.Y + dir.Y * radius);
                break;
            }
        }

        _ = firstShift;
    }

    private static (double X, double Y) Unit((double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var l = Math.Sqrt(dx * dx + dy * dy);
        return l < 1e-9 ? (1, 0) : (dx / l, dy / l);
    }

    private static void Apply(Shape dim, List<(double X, double Y)> abs)
    {
        dim.X = abs[0].X;
        dim.Y = abs[0].Y;
        dim.Points = abs.Select(p => new CorePoint(p.X - dim.X, p.Y - dim.Y)).ToList();
        PathBoundsCalculator.Recompute(dim);
    }

    private static bool Matches(List<(double X, double Y)> a, List<(double X, double Y)> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (Math.Abs(a[i].X - b[i].X) > Same || Math.Abs(a[i].Y - b[i].Y) > Same)
            {
                return false;
            }
        }

        return true;
    }
}
