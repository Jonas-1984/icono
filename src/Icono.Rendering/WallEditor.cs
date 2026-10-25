using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Cutting walls apart: at a clicked position, at a chosen point, or at every point.</summary>
public static class WallEditor
{
    /// <summary>A place on the wall's centre line.</summary>
    public readonly record struct Spot(int Segment, double T, double X, double Y, double Distance, bool IsVertex, int VertexIndex);

    /// <summary>The look a freshly made wall starts with: a light band with a diagonal hatch and a dark outline. Everything can be
    /// changed in the WALL card.</summary>
    public static void ApplyDrawingDefaults(Shape wall)
    {
        wall.FillColor = ArgbColor.FromRgb(0xFA, 0xFA, 0xFA);
        wall.BorderColor = ArgbColor.FromRgb(0x23, 0x29, 0x33);
        wall.BorderThickness = 1.5;
        wall.DimOffset = 14; // the dimension line of a wall sits this far from its outer face
    }

    /// <summary>A corner point that was moved: where it was and by how much.</summary>
    public readonly record struct Move(double FromX, double FromY, double DeltaX, double DeltaY);

    /// <summary>Makes one segment of the wall <paramref name="newLength"/> long (page px) by moving its END point along the segment. Walls
    /// that run at a right angle to it slide along (the end points of the chain of perpendicular segments move by the same amount),
    /// so a rectangular room stays rectangular when one side is changed; any other wall just changes its angle. Returns the corner
    /// points that moved (empty when nothing changed).</summary>
    public static List<Move> ResizeSegment(Shape wall, int segment, double newLength)
    {
        var moves = new List<Move>();
        if (wall.Points is not { Count: >= 2 } local || newLength < 0.5)
        {
            return moves;
        }

        var n = local.Count;
        var closed = wall.WallClosed && n >= 3;
        if (segment < 0 || segment >= (closed ? n : n - 1))
        {
            return moves;
        }

        var points = local.Select(p => (X: wall.X + p.X, Y: wall.Y + p.Y)).ToList();
        var start = segment;
        var end = (segment + 1) % n;
        var length = Math.Sqrt(Math.Pow(points[end].X - points[start].X, 2) + Math.Pow(points[end].Y - points[start].Y, 2));
        if (length < 1e-6)
        {
            return moves;
        }

        var ux = (points[end].X - points[start].X) / length;
        var uy = (points[end].Y - points[start].Y) / length;
        var dx = ux * (newLength - length);
        var dy = uy * (newLength - length);
        if (Math.Abs(dx) + Math.Abs(dy) < 1e-9)
        {
            return moves;
        }

        var original = points.ToList();
        var k = end;
        while (true)
        {
            points[k] = (original[k].X + dx, original[k].Y + dy);
            moves.Add(new Move(original[k].X, original[k].Y, dx, dy));

            var next = (k + 1) % n;
            var hasNext = closed || k + 1 < n;
            if (!hasNext || next == start)
            {
                break;
            }

            var sx = original[next].X - original[k].X;
            var sy = original[next].Y - original[k].Y;
            var segmentLength = Math.Sqrt(sx * sx + sy * sy);
            if (segmentLength < 1e-6 || Math.Abs((sx * ux + sy * uy) / segmentLength) > 0.02)
            {
                break; // not at a right angle: this wall only changes its angle
            }

            k = next;
        }

        wall.Points = points.Select(p => new CorePoint(p.X - wall.X, p.Y - wall.Y)).ToList();
        PathBoundsCalculator.Recompute(wall);
        return moves;
    }

    /// <summary>Moves the corner points of <paramref name="other"/> that lay on a point moved by <see cref="ResizeSegment"/> by the
    /// same amount, so walls that were joined stay joined. Returns true when the wall changed.</summary>
    public static bool FollowMoves(Shape other, IReadOnlyList<Move> moves)
    {
        if (other.Points is not { Count: > 0 } local || moves.Count == 0)
        {
            return false;
        }

        var changed = false;
        var points = local.Select(p => (X: other.X + p.X, Y: other.Y + p.Y)).ToList();
        for (var i = 0; i < points.Count; i++)
        {
            foreach (var move in moves)
            {
                if (Math.Abs(points[i].X - move.FromX) < 0.75 && Math.Abs(points[i].Y - move.FromY) < 0.75)
                {
                    points[i] = (points[i].X + move.DeltaX, points[i].Y + move.DeltaY);
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            other.Points = points.Select(p => new CorePoint(p.X - other.X, p.Y - other.Y)).ToList();
            PathBoundsCalculator.Recompute(other);
        }

        return changed;
    }

    /// <summary>Closest place on the centre line to <paramref name="x"/>,<paramref name="y"/>. Positions within
    /// <paramref name="vertexReach"/> of a corner point count as that point.</summary>
    public static Spot? Nearest(Shape wall, double x, double y, double vertexReach)
    {
        var points = WallGeometry.AbsolutePoints(wall);
        if (points.Count < 2)
        {
            return null;
        }

        var closed = wall.WallClosed && points.Count >= 3;
        var segmentCount = closed ? points.Count : points.Count - 1;

        Spot? best = null;
        for (var i = 0; i < segmentCount; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared < 1e-9 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared, 0, 1);
            var px = a.X + t * dx;
            var py = a.Y + t * dy;
            var distance = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));

            if (best is null || distance < best.Value.Distance)
            {
                best = new Spot(i, t, px, py, distance, false, -1);
            }
        }

        if (best is not { } found)
        {
            return null;
        }

        // Close to a corner (or an end): the spot is that point.
        for (var i = 0; i < points.Count; i++)
        {
            var distance = Math.Sqrt((x - points[i].X) * (x - points[i].X) + (y - points[i].Y) * (y - points[i].Y));
            if (distance <= vertexReach && distance <= found.Distance + vertexReach)
            {
                return new Spot(found.Segment, found.T, points[i].X, points[i].Y, distance, true, i);
            }
        }

        return found;
    }

    /// <summary>Cuts the wall at <paramref name="spot"/>. Returns the walls that replace it (new shapes, the original is
    /// untouched), or an empty list when nothing can be cut there (an end of an open wall).</summary>
    public static List<Shape> SplitAt(Shape wall, Spot spot)
    {
        var points = WallGeometry.AbsolutePoints(wall).Select(p => (X: (double)p.X, Y: (double)p.Y)).ToList();
        var closed = wall.WallClosed && points.Count >= 3;
        var result = new List<Shape>();

        if (closed)
        {
            List<(double X, double Y)> ring;
            if (spot.IsVertex)
            {
                var k = spot.VertexIndex;
                ring = [];
                for (var i = 0; i <= points.Count; i++)
                {
                    ring.Add(points[(k + i) % points.Count]);
                }
            }
            else
            {
                var cut = (spot.X, spot.Y);
                ring = [cut];
                for (var i = 1; i <= points.Count; i++)
                {
                    ring.Add(points[(spot.Segment + i) % points.Count]);
                }

                ring.Add(cut);
            }

            result.Add(MakeWall(wall, ring, closed: false));
            return result;
        }

        if (spot.IsVertex)
        {
            var k = spot.VertexIndex;
            if (k <= 0 || k >= points.Count - 1)
            {
                return result;
            }

            result.Add(MakeWall(wall, points.GetRange(0, k + 1), closed: false));
            result.Add(MakeWall(wall, points.GetRange(k, points.Count - k), closed: false));
            return result;
        }

        var first = points.GetRange(0, spot.Segment + 1);
        first.Add((spot.X, spot.Y));
        var second = new List<(double X, double Y)> { (spot.X, spot.Y) };
        second.AddRange(points.GetRange(spot.Segment + 1, points.Count - spot.Segment - 1));

        result.Add(MakeWall(wall, first, closed: false));
        result.Add(MakeWall(wall, second, closed: false));
        return result;
    }

    // ----- keeping the per-dimension settings on the right segments when points are added or removed ----------

    /// <summary>After a point was inserted after vertex <paramref name="afterIndex"/> (that segment was split in two): the settings stay on
    /// the first half, later segments move up by one.</summary>
    public static void RemapOverridesAfterInsert(Shape wall, int afterIndex) =>
        Remap(wall, i => i <= afterIndex ? i : i + 1);

    /// <summary>Call BEFORE vertex <paramref name="removedIndex"/> is removed: the two segments next to it become one; the settings of the
    /// segment that disappears are dropped and the later segments move down by one.</summary>
    public static void RemapOverridesBeforeRemove(Shape wall, int removedIndex)
    {
        var count = wall.Points?.Count ?? 0;
        var closed = wall.WallClosed && count >= 3;
        Func<int, int?> map;
        if (removedIndex == 0)
        {
            map = i => i == 0 ? null : i - 1;
        }
        else if (!closed && removedIndex == count - 1)
        {
            map = i => i == count - 2 ? null : i;
        }
        else
        {
            map = i => i < removedIndex ? i : i == removedIndex ? null : i - 1;
        }

        Remap(wall, map);
    }

    /// <summary>Drops settings of segments the wall no longer has (after opening a ring or removing points).</summary>
    public static void PruneOverrides(Shape wall)
    {
        var segments = WallGeometry.Segments(wall).Count;
        Remap(wall, i => i < segments ? i : null);
    }

    private static void Remap(Shape wall, Func<int, int?> map)
    {
        if (wall.WallDimensionOverrides is not { Count: > 0 } overrides)
        {
            return;
        }

        var moved = new List<WallDimensionOverride>();
        foreach (var o in overrides)
        {
            if (map(o.Segment) is { } index)
            {
                moved.Add(o with { Segment = index });
            }
        }

        wall.WallDimensionOverrides = moved.Count == 0 ? null : moved.OrderBy(o => o.Segment).ToList();
    }

    /// <summary>Breaks the wall into one wall per segment (cut at every point).</summary>
    public static List<Shape> SplitAtAllPoints(Shape wall)
    {
        var points = WallGeometry.AbsolutePoints(wall).Select(p => (X: (double)p.X, Y: (double)p.Y)).ToList();
        var result = new List<Shape>();
        var closed = wall.WallClosed && points.Count >= 3;
        var count = closed ? points.Count : points.Count - 1;

        for (var i = 0; i < count; i++)
        {
            result.Add(MakeWall(wall, [points[i], points[(i + 1) % points.Count]], closed: false));
        }

        return result;
    }

    /// <summary>A new wall with the look of <paramref name="template"/> running through <paramref name="absolutePoints"/>.</summary>
    private static Shape MakeWall(Shape template, List<(double X, double Y)> absolutePoints, bool closed)
    {
        var wall = new Shape { Kind = ShapeKind.Wall };
        ShapeSnapshot.Capture(template).ApplyTo(wall);
        wall.StartAnchor = null;
        wall.WallDimensionOverrides = null; // segment numbers change when a wall is cut
        wall.EndAnchor = null;
        wall.WallClosed = closed;
        wall.Rotation = 0;

        wall.X = absolutePoints[0].X;
        wall.Y = absolutePoints[0].Y;
        wall.Points = absolutePoints.Select(p => new CorePoint(p.X - wall.X, p.Y - wall.Y)).ToList();
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }
}
