using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>A line to show while a corner of a wall is dragged and snaps: from <see cref="A"/> to <see cref="B"/>.</summary>
public sealed record VertexGuide(SKPoint A, SKPoint B, bool Straight);

/// <summary>Where a dragged corner lands, and what to show.</summary>
public sealed record VertexSnap(SKPoint Point, IReadOnlyList<VertexGuide> Guides)
{
    public bool Snapped => Guides.Count > 0;
}

/// <summary>Making walls straight: moving a corner onto the line between its neighbours, merging walls that run straight on into one wall,
/// and the magnet that does the first while a corner is dragged.</summary>
public static class WallStraightener
{
    /// <summary>The neighbours of a corner: the previous and the next one (a ring wraps round; the ends of an open wall have only one).</summary>
    public static (int Previous, int Next)? Neighbours(Shape wall, int index)
    {
        var count = wall.Points?.Count ?? 0;
        var closed = wall.WallClosed && count >= 3;
        if (index < 0 || index >= count)
        {
            return null;
        }

        if (!closed && (index == 0 || index == count - 1))
        {
            return null;
        }

        return ((index - 1 + count) % count, (index + 1) % count);
    }

    /// <summary>Moves the corner onto the straight line between its two neighbours, so the two walls at it run on as one straight wall.</summary>
    public static bool StraightenCorner(Shape wall, int index)
    {
        if (Neighbours(wall, index) is not { } n)
        {
            return false;
        }

        var points = WallGeometry.AbsolutePoints(wall);
        var onLine = ClosestOnLine(points[n.Previous], points[n.Next], points[index]);
        if (Geo.Distance(onLine, points[index]) < 1e-4f)
        {
            return false;
        }

        points[index] = onLine;
        wall.Points = points.Select(p => new CorePoint(p.X - wall.X, p.Y - wall.Y)).ToList();
        PathBoundsCalculator.Recompute(wall);
        return true;
    }

    /// <summary>Removes the corners where the wall runs straight on (the turn is at most <paramref name="toleranceDegrees"/>), so two walls
    /// in one line become one wall. Returns how many corners were removed.</summary>
    public static int MergeStraightParts(Shape wall, double toleranceDegrees = 1.0)
    {
        var removed = 0;
        while (wall.Points is { } local)
        {
            var closed = wall.WallClosed && local.Count >= 3;
            var minimum = closed ? 3 : 2;
            if (local.Count <= minimum)
            {
                break;
            }

            var points = WallGeometry.AbsolutePoints(wall);
            var found = -1;
            for (var i = 0; i < points.Count && found < 0; i++)
            {
                if (Neighbours(wall, i) is { } n && TurnDegrees(points[n.Previous], points[i], points[n.Next]) <= toleranceDegrees)
                {
                    found = i;
                }
            }

            if (found < 0)
            {
                break;
            }

            WallEditor.RemapOverridesBeforeRemove(wall, found);
            var kept = local.ToList();
            kept.RemoveAt(found);
            wall.Points = kept;
            PathBoundsCalculator.Recompute(wall);
            removed++;
        }

        return removed;
    }

    /// <summary>The angle (degrees) the wall turns by at <paramref name="corner"/>: 0 = straight on.</summary>
    public static double TurnDegrees(SKPoint previous, SKPoint corner, SKPoint next)
    {
        double ax = corner.X - previous.X, ay = corner.Y - previous.Y;
        double bx = next.X - corner.X, by = next.Y - corner.Y;
        var la = Math.Sqrt(ax * ax + ay * ay);
        var lb = Math.Sqrt(bx * bx + by * by);
        if (la < 1e-9 || lb < 1e-9)
        {
            return 0;
        }

        var cos = Math.Clamp((ax * bx + ay * by) / (la * lb), -1, 1);
        return Math.Acos(cos) * 180 / Math.PI;
    }

    private static SKPoint ClosestOnLine(SKPoint a, SKPoint b, SKPoint p)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9f)
        {
            return a;
        }

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
        return new SKPoint(a.X + t * dx, a.Y + t * dy);
    }

    /// <summary>The magnet for a corner being dragged to <paramref name="target"/>: onto the line between its two neighbours (so the walls
    /// become straight), and in line horizontally or vertically with its neighbours and the corners of other walls.</summary>
    public static VertexSnap Snap(Document document, Shape wall, int index, SKPoint target, double tolerance)
    {
        var points = WallGeometry.AbsolutePoints(wall);
        var guides = new List<VertexGuide>();
        var tol = (float)tolerance;

        // 1. straight: on the line through the two neighbours
        if (Neighbours(wall, index) is { } n)
        {
            var a = points[n.Previous];
            var b = points[n.Next];
            var onLine = ClosestOnLine(a, b, target);
            if (Geo.Distance(onLine, target) <= tol)
            {
                guides.Add(new VertexGuide(a, b, Straight: true));
                return new VertexSnap(onLine, guides);
            }
        }

        // 2. lined up with other corners (the neighbours first, then every corner of every wall)
        var others = new List<SKPoint>();
        if (Neighbours(wall, index) is { } m)
        {
            others.Add(points[m.Previous]);
            others.Add(points[m.Next]);
        }
        else
        {
            // an end of an open wall: its only neighbour
            if (index > 0)
            {
                others.Add(points[index - 1]);
            }

            if (index + 1 < points.Count)
            {
                others.Add(points[index + 1]);
            }
        }

        foreach (var other in document.VisibleShapes().Where(s => s.Kind == ShapeKind.Wall && !ReferenceEquals(s, wall) && s.Points is not null))
        {
            others.AddRange(WallGeometry.AbsolutePoints(other));
        }

        var result = target;
        SKPoint? snapX = null;
        SKPoint? snapY = null;
        float bestX = tol + 1, bestY = tol + 1;
        foreach (var other in others)
        {
            var dx = MathF.Abs(other.X - target.X);
            var dy = MathF.Abs(other.Y - target.Y);
            if (dx <= tol && dx < bestX)
            {
                bestX = dx;
                snapX = other;
            }

            if (dy <= tol && dy < bestY)
            {
                bestY = dy;
                snapY = other;
            }
        }

        if (snapX is { } sx)
        {
            result = new SKPoint(sx.X, result.Y);
        }

        if (snapY is { } sy)
        {
            result = new SKPoint(result.X, sy.Y);
        }

        if (snapX is { } gx)
        {
            guides.Add(new VertexGuide(gx, result, Straight: false));
        }

        if (snapY is { } gy)
        {
            guides.Add(new VertexGuide(gy, result, Straight: false));
        }

        return new VertexSnap(result, guides);
    }
}
