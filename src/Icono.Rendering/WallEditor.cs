using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Cutting walls apart: at a clicked position, at a chosen point, or at every point.</summary>
public static class WallEditor
{
    /// <summary>A place on the wall's centre line.</summary>
    public readonly record struct Spot(int Segment, double T, double X, double Y, double Distance, bool IsVertex, int VertexIndex);

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
