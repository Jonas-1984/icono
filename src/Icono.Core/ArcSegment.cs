namespace Icono.Core;

/// <summary>An arc as one side of a polyline or polygon (<see cref="Shape.SegmentBulges"/>), described the way AutoCAD does: by its two end points and its
/// <i>bulge</i> = tan(a quarter of the angle the arc covers). 0 is a straight side; the sign tells on which side of the chord the arc lies (positive: to the
/// left of the way from the first to the second point as seen on the screen with y pointing down, i.e. cross(b - a, middle - a) &gt; 0). The drawing tools
/// make a bulge from three points - the start, a point on the arc and the end.</summary>
public static class ArcSegment
{
    /// <summary>A bulge below this is a straight side.</summary>
    public const double Epsilon = 1e-6;

    /// <summary>The largest bulge that is kept (a nearly closed circle): the arc of three points that lie almost in one line the other way round would need an
    /// endless one.</summary>
    public const double MaxBulge = 1e4;

    /// <summary>The circle of an arc: its centre and radius, the angle (atan2, y down) of the first point and the signed angle the arc covers from there
    /// (positive: the angle grows, which is clockwise on the screen).</summary>
    public readonly record struct Arc(CorePoint Center, double Radius, double StartAngle, double Sweep);

    /// <summary>The bulge of the arc that starts in <paramref name="a"/>, passes through <paramref name="m"/> and ends in <paramref name="b"/>; 0 when the three
    /// points lie in one line (or two of them coincide).</summary>
    public static double FromThreePoints(CorePoint a, CorePoint m, CorePoint b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double amx = m.X - a.X, amy = m.Y - a.Y;
        var cross = abx * amy - aby * amx;
        double lengthAm = Math.Sqrt(amx * amx + amy * amy);
        double lengthAb = Math.Sqrt(abx * abx + aby * aby);
        if (lengthAm < 1e-9 || lengthAb < 1e-9 || Math.Abs(cross) < 1e-9 * lengthAm * lengthAb + 1e-12)
        {
            return 0;
        }

        // the angle at m between a and b is an inscribed angle: the arc a -> m -> b is the one the angle does not look at, so it covers 2 pi - 2 alpha
        double mx = a.X - m.X, my = a.Y - m.Y;
        double nx = b.X - m.X, ny = b.Y - m.Y;
        var cosAlpha = Math.Clamp((mx * nx + my * ny) / (Math.Sqrt(mx * mx + my * my) * Math.Sqrt(nx * nx + ny * ny)), -1, 1);
        var alpha = Math.Acos(cosAlpha);
        var covered = 2 * Math.PI - 2 * alpha;
        var bulge = Math.Min(MaxBulge, Math.Tan(covered / 4));
        return cross > 0 ? bulge : -bulge;
    }

    /// <summary>True when the side is an arc (a bulge that is not zero).</summary>
    public static bool IsArc(double bulge) => Math.Abs(bulge) > Epsilon;

    /// <summary>The circle of the arc between two points, or null for a straight side.</summary>
    public static Arc? Describe(CorePoint a, CorePoint b, double bulge)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var chord = Math.Sqrt(dx * dx + dy * dy);
        if (!IsArc(bulge) || chord < 1e-9)
        {
            return null;
        }

        var absolute = Math.Abs(bulge);
        var covered = 4 * Math.Atan(absolute);
        var radius = chord * (1 + absolute * absolute) / (4 * absolute);
        var sagitta = chord / 2 * absolute;

        // the normal that points to the side of the arc (cross(b - a, normal) > 0), from the middle of the chord to the centre
        double ux = dx / chord, uy = dy / chord;
        var sign = bulge > 0 ? 1.0 : -1.0;
        double nx = -uy * sign, ny = ux * sign;
        var along = sagitta - radius;
        var center = new CorePoint((a.X + b.X) / 2 + nx * along, (a.Y + b.Y) / 2 + ny * along);

        var start = Math.Atan2(a.Y - center.Y, a.X - center.X);

        // a positive bulge goes counter-clockwise on the screen, which is an angle that gets smaller (y points down)
        return new Arc(center, radius, start, bulge > 0 ? -covered : covered);
    }

    /// <summary>The point of the arc at <paramref name="fraction"/> (0 = first point, 1 = second) of the way round it.</summary>
    public static CorePoint PointAt(Arc arc, double fraction)
    {
        var angle = arc.StartAngle + arc.Sweep * fraction;
        return new CorePoint(arc.Center.X + arc.Radius * Math.Cos(angle), arc.Center.Y + arc.Radius * Math.Sin(angle));
    }

    /// <summary>The points of an arc that lie furthest left, right, up and down (only those that are on the arc itself): they belong to the bounds of a shape.</summary>
    public static IEnumerable<CorePoint> ExtremePoints(CorePoint a, CorePoint b, double bulge)
    {
        if (Describe(a, b, bulge) is not { } arc)
        {
            yield break;
        }

        var direction = Math.Sign(arc.Sweep);
        var covered = Math.Abs(arc.Sweep);
        for (var quarter = 0; quarter < 4; quarter++)
        {
            var angle = quarter * Math.PI / 2;
            var distance = Normalize((angle - arc.StartAngle) * direction);
            if (distance <= covered)
            {
                yield return new CorePoint(arc.Center.X + arc.Radius * Math.Cos(angle), arc.Center.Y + arc.Radius * Math.Sin(angle));
            }
        }
    }

    /// <summary>The direction (a unit vector) in which the arc leaves its first point, and the one in which it arrives in its second point - for the arrow heads
    /// at the ends of a line with arcs. A straight side has the direction of its chord at both ends.</summary>
    public static ((double X, double Y) Start, (double X, double Y) End) Tangents(CorePoint a, CorePoint b, double bulge)
    {
        if (Describe(a, b, bulge) is not { } arc)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var length = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
            return ((dx / length, dy / length), (dx / length, dy / length));
        }

        var direction = Math.Sign(arc.Sweep);
        (double X, double Y) At(CorePoint p) => (-direction * (p.Y - arc.Center.Y) / arc.Radius, direction * (p.X - arc.Center.X) / arc.Radius);
        return (At(a), At(b));
    }

    /// <summary>The point of the arc nearest to (<paramref name="x"/>, <paramref name="y"/>), how far it is, and its place on the arc as a fraction (0 to 1) of
    /// the way from the first to the second point. A straight side is handled like a line.</summary>
    public static (CorePoint Point, double Distance, double Fraction) Nearest(CorePoint a, CorePoint b, double bulge, double x, double y)
    {
        if (Describe(a, b, bulge) is not { } arc)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared < 1e-9 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared, 0, 1);
            var point = new CorePoint(a.X + t * dx, a.Y + t * dy);
            return (point, Math.Sqrt((x - point.X) * (x - point.X) + (y - point.Y) * (y - point.Y)), t);
        }

        var direction = Math.Sign(arc.Sweep);
        var covered = Math.Abs(arc.Sweep);
        var angle = Math.Atan2(y - arc.Center.Y, x - arc.Center.X);
        var distanceAlong = Normalize((angle - arc.StartAngle) * direction);
        double fraction;
        if (distanceAlong <= covered)
        {
            fraction = distanceAlong / covered;
        }
        else
        {
            // beside the arc: the nearer of its two ends
            var toStart = Math.Min(distanceAlong, 2 * Math.PI - distanceAlong);
            var toEnd = distanceAlong - covered;
            fraction = Math.Min(toEnd, 2 * Math.PI - toEnd) < toStart ? 1 : 0;
        }

        var nearest = PointAt(arc, fraction);
        return (nearest, Math.Sqrt((x - nearest.X) * (x - nearest.X) + (y - nearest.Y) * (y - nearest.Y)), fraction);
    }

    /// <summary>The bulges of the two arcs a point at <paramref name="fraction"/> of the arc divides it into (the two together are the same arc).</summary>
    public static (double First, double Second) Split(double bulge, double fraction)
    {
        if (!IsArc(bulge))
        {
            return (0, 0);
        }

        var covered = 4 * Math.Atan(Math.Abs(bulge));
        var sign = bulge > 0 ? 1.0 : -1.0;
        return (sign * Math.Tan(covered * fraction / 4), sign * Math.Tan(covered * (1 - fraction) / 4));
    }

    /// <summary>The bulges after a point was put into the side <paramref name="after"/> at <paramref name="fraction"/> of the way along it: an arc is divided into two
    /// arcs of the same circle, a straight side into two straight sides. Null when no side is an arc any more (or the list did not fit the points).</summary>
    public static IReadOnlyList<double>? AfterInsert(IReadOnlyList<double>? bulges, int oldCount, int after, double fraction)
    {
        if (bulges is null || bulges.Count != oldCount || after < 0 || after >= oldCount)
        {
            return null;
        }

        var list = bulges.ToList();
        var (first, second) = Split(list[after], fraction);
        list[after] = first;
        list.Insert(after + 1, second);
        return list.Any(IsArc) ? list : null;
    }

    /// <summary>The bulges after the point <paramref name="removed"/> was taken out: the two sides next to it become one straight side. Null when no side is an arc any
    /// more (or the list did not fit the points).</summary>
    public static IReadOnlyList<double>? AfterRemove(IReadOnlyList<double>? bulges, int oldCount, int removed, bool closed)
    {
        if (bulges is null || bulges.Count != oldCount || removed < 0 || removed >= oldCount)
        {
            return null;
        }

        var list = bulges.ToList();
        var before = removed > 0 ? removed - 1 : closed ? oldCount - 1 : -1;
        if (before >= 0)
        {
            list[before] = 0;
        }

        list.RemoveAt(removed);
        return list.Any(IsArc) ? list : null;
    }

    private static double Normalize(double angle)
    {
        var turn = 2 * Math.PI;
        angle %= turn;
        return angle < 0 ? angle + turn : angle;
    }
}
