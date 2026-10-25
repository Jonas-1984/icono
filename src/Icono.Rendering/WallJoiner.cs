using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>What a wall needs to be drawn as part of a joint: extra area that closes the corner, and, for ONE wall of a connected
/// group, the common outline whose border is drawn once so no seam line runs through the joints.</summary>
public sealed class WallJoin : IDisposable
{
    /// <summary>Mitre area added to this wall so it meets the walls that end at the same point in a clean corner.</summary>
    public SKPath? Patch { get; set; }

    /// <summary>Outline of the whole connected group; set only on the wall that is drawn last (its border is the group's border).
    /// The other walls of the group have a join without it and draw no border of their own.</summary>
    public SKPath? GroupOutline { get; set; }

    public void Dispose()
    {
        Patch?.Dispose();
        GroupOutline?.Dispose();
    }
}

/// <summary>
/// Walls that are drawn one by one (each stroke of the wall tool is its own wall) used to meet in two butt-ended bands: a notch on the
/// outside of the corner and the end lines of both bands visible inside the joint. This finds walls that touch - end to end, or an
/// end on the side of another wall - and lets the renderer draw them as one piece: mitred corners and one common border.
/// Only walls with the same border colour, thickness and opacity are joined, so differently styled walls stay as they are.
/// </summary>
public static class WallJoiner
{
    private const float MiterLimit = 8f;

    [ThreadStatic]
    private static Dictionary<Shape, WallJoin>? _current;

    /// <summary>The join of a wall of the document being rendered, or null (not touching another wall, or rendered on its own).</summary>
    public static WallJoin? For(Shape wall) => _current is not null && _current.TryGetValue(wall, out var join) ? join : null;

    /// <summary>Computes the joins of the document; they apply until the returned scope is disposed.</summary>
    public static IDisposable Begin(Document document)
    {
        var scope = new Scope(_current, Compute(document));
        _current = scope.Joins;
        return scope;
    }

    private sealed class Scope(Dictionary<Shape, WallJoin>? previous, Dictionary<Shape, WallJoin> joins) : IDisposable
    {
        public Dictionary<Shape, WallJoin> Joins { get; } = joins;

        public void Dispose()
        {
            _current = previous;
            foreach (var join in Joins.Values)
            {
                join.Dispose();
            }
        }
    }

    private sealed class Info(Shape wall, List<SKPoint> points)
    {
        public Shape Wall { get; } = wall;

        public List<SKPoint> Points { get; } = points;

        public float Half { get; } = (float)Math.Max(0.25, wall.WallThickness / 2);

        public bool Closed => Wall.WallClosed && Points.Count >= 3;
    }

    private readonly record struct End(int Wall, SKPoint Position, SKPoint Direction);

    private static Dictionary<Shape, WallJoin> Compute(Document document)
    {
        var joins = new Dictionary<Shape, WallJoin>();

        // in drawing order: the last wall of a group is drawn on top of the others, so it draws the common border
        var walls = new List<Info>();
        for (var i = document.Layers.Count - 1; i >= 0; i--)
        {
            if (!document.Layers[i].Visible)
            {
                continue;
            }

            var shapes = document.Layers[i].Shapes;
            for (var j = shapes.Count - 1; j >= 0; j--)
            {
                if (shapes[j] is { Kind: ShapeKind.Wall, Points.Count: >= 2 } wall)
                {
                    walls.Add(new Info(wall, PlacedPoints(wall)));
                }
            }
        }

        if (walls.Count < 2)
        {
            return joins;
        }

        var group = Enumerable.Range(0, walls.Count).ToArray();
        int Find(int x) => group[x] == x ? x : group[x] = Find(group[x]);
        void Union(int a, int b) => group[Find(a)] = Find(b);

        var ends = new List<End>();
        for (var i = 0; i < walls.Count; i++)
        {
            if (!walls[i].Closed)
            {
                AddEnds(ends, i, walls[i].Points);
            }
        }

        // an end that touches the side of another wall (T-joint) belongs to the same group
        foreach (var end in ends)
        {
            for (var j = 0; j < walls.Count; j++)
            {
                if (j == end.Wall || !SameBorder(walls[end.Wall].Wall, walls[j].Wall) || Find(end.Wall) == Find(j))
                {
                    continue;
                }

                if (Touches(end.Position, walls[j], Tolerance(walls[end.Wall], walls[j])))
                {
                    Union(end.Wall, j);
                }
            }
        }

        // ends that meet at one point: close the corners between neighbouring walls
        var clustered = new bool[ends.Count];
        var patches = new Dictionary<int, SKPath>();
        for (var a = 0; a < ends.Count; a++)
        {
            if (clustered[a])
            {
                continue;
            }

            var cluster = new List<int> { a };
            clustered[a] = true;
            for (var b = a + 1; b < ends.Count; b++)
            {
                if (!clustered[b] && ends[b].Wall != ends[a].Wall &&
                    Geo.Distance(ends[a].Position, ends[b].Position) <= Tolerance(walls[ends[a].Wall], walls[ends[b].Wall]) &&
                    cluster.All(c => ends[c].Wall != ends[b].Wall && SameBorder(walls[ends[c].Wall].Wall, walls[ends[b].Wall].Wall)))
                {
                    cluster.Add(b);
                    clustered[b] = true;
                }
            }

            if (cluster.Count < 2)
            {
                continue;
            }

            foreach (var member in cluster)
            {
                Union(ends[cluster[0]].Wall, ends[member].Wall);
            }

            var around = cluster.OrderBy(c => Math.Atan2(ends[c].Direction.Y, ends[c].Direction.X)).ToList();
            var centre = ends[cluster[0]].Position;
            for (var k = 0; k < around.Count; k++)
            {
                var first = ends[around[k]];
                var second = ends[around[(k + 1) % around.Count]];
                if (first.Wall == second.Wall)
                {
                    continue;
                }

                var piece = Corner(centre, first.Direction, walls[first.Wall].Half, second.Direction, walls[second.Wall].Half);
                if (patches.TryGetValue(first.Wall, out var existing))
                {
                    existing.AddPath(piece);
                    piece.Dispose();
                }
                else
                {
                    patches[first.Wall] = piece;
                }
            }
        }

        foreach (var (index, patch) in patches)
        {
            joins[walls[index].Wall] = new WallJoin { Patch = patch };
        }

        // one common outline per connected group, with the border drawn by its last wall
        var components = Enumerable.Range(0, walls.Count).GroupBy(Find).Where(g => g.Count() > 1);
        foreach (var component in components)
        {
            SKPath? union = null;
            foreach (var index in component)
            {
                if (!joins.ContainsKey(walls[index].Wall))
                {
                    joins[walls[index].Wall] = new WallJoin();
                }

                using var outline = WallGeometry.BuildOutline(walls[index].Wall);
                using var piece = new SKPath(outline);
                if (patches.TryGetValue(index, out var patch))
                {
                    piece.AddPath(patch);
                }

                var next = union is null ? new SKPath(piece) : union.Op(piece, SKPathOp.Union) ?? union;
                if (!ReferenceEquals(next, union))
                {
                    union?.Dispose();
                }

                union = next;
            }

            if (union is null)
            {
                continue;
            }

            joins[walls[component.Max()].Wall].GroupOutline = WallGeometry.Merge(union);
        }

        return joins;
    }

    /// <summary>The wall's points where they are drawn (turned with the wall).</summary>
    private static List<SKPoint> PlacedPoints(Shape wall)
    {
        var points = WallGeometry.AbsolutePoints(wall);
        if (wall.Rotation != 0)
        {
            var matrix = SKMatrix.CreateRotationDegrees((float)wall.Rotation, (float)(wall.X + wall.Width / 2), (float)(wall.Y + wall.Height / 2));
            for (var i = 0; i < points.Count; i++)
            {
                points[i] = matrix.MapPoint(points[i]);
            }
        }

        return points;
    }

    private static void AddEnds(List<End> ends, int wall, List<SKPoint> points)
    {
        if (Direction(points[0], points[1]) is { } start)
        {
            ends.Add(new End(wall, points[0], start));
        }

        if (Direction(points[^1], points[^2]) is { } finish)
        {
            ends.Add(new End(wall, points[^1], finish));
        }
    }

    /// <summary>Unit vector from <paramref name="from"/> to <paramref name="to"/>; null for two equal points.</summary>
    private static SKPoint? Direction(SKPoint from, SKPoint to)
    {
        var length = Geo.Distance(from, to);
        return length < 1e-3f ? null : new SKPoint((to.X - from.X) / length, (to.Y - from.Y) / length);
    }

    private static float Tolerance(Info a, Info b) => Math.Max(1.5f, 0.5f * Math.Min(a.Half, b.Half));

    private static bool Touches(SKPoint point, Info wall, float tolerance)
    {
        var count = wall.Closed ? wall.Points.Count : wall.Points.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var from = wall.Points[i];
            var to = wall.Points[(i + 1) % wall.Points.Count];
            if (Geo.Distance(point, Geo.ClosestOnSegment(from, to, point)) <= tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameBorder(Shape a, Shape b) =>
        a.BorderColor == b.BorderColor && Math.Abs(a.BorderThickness - b.BorderThickness) < 1e-6 && Math.Abs(a.Opacity - b.Opacity) < 1e-6;

    /// <summary>The area between two bands that start at <paramref name="p"/> and run in the directions d1 and d2 (d2 comes after d1 when
    /// turning counter-clockwise): the quad up to the point where their facing edges meet (a mitred corner), or just the triangle between
    /// the two end faces when that point is too far away (very sharp angle) or the walls run on in a straight line.</summary>
    private static SKPath Corner(SKPoint p, SKPoint d1, float h1, SKPoint d2, float h2)
    {
        // the edge of each band that faces the gap between the two directions
        var n1 = new SKPoint(-d1.Y, d1.X);
        var n2 = new SKPoint(d2.Y, -d2.X);
        var a = new SKPoint(p.X + n1.X * h1, p.Y + n1.Y * h1);
        var b = new SKPoint(p.X + n2.X * h2, p.Y + n2.Y * h2);

        var path = new SKPath();
        path.MoveTo(p);
        path.LineTo(a);

        var cross = d1.X * d2.Y - d1.Y * d2.X;
        if (Math.Abs(cross) > 1e-3f)
        {
            var t = ((b.X - a.X) * d2.Y - (b.Y - a.Y) * d2.X) / cross;
            var tip = new SKPoint(a.X + t * d1.X, a.Y + t * d1.Y);
            if (Geo.Distance(tip, p) <= MiterLimit * Math.Max(h1, h2))
            {
                path.LineTo(tip);
            }
        }

        path.LineTo(b);
        path.Close();
        return path;
    }
}
