using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// The number typed while a wall is drawn is the length the wall's dimension shows (by default the inside one, from face to face), not the
/// length of its centre line: a wall that starts at a corner gives part of its centre line to the wall it meets. These helpers work on the
/// absolute corner points of the wall (page px) and make the centre line as long as the typed number needs, so a room typed as 400 x 300
/// really is 400 x 300 inside.
/// </summary>
public static class WallTypedLength
{
    /// <summary>The length a dimension of <paramref name="segment"/> shows for these corner points, with the wall's own settings
    /// (inside / outside / centre measure, thickness).</summary>
    public static double Measured(Shape wall, IReadOnlyList<SKPoint> points, int segment, bool closed = false)
    {
        var probe = new Shape { Kind = ShapeKind.Wall };
        ShapeSnapshot.Capture(wall).ApplyTo(probe);
        probe.X = 0;
        probe.Y = 0;
        probe.WallClosed = closed;
        probe.Points = points.Select(p => new CorePoint(p.X, p.Y)).ToList();
        return WallGeometry.MeasuredLength(probe, segment);
    }

    /// <summary>The corner at the end of the wall being drawn (the last of <paramref name="points"/> is its moving end): in direction
    /// (<paramref name="ux"/>, <paramref name="uy"/>) from the corner before it, far enough that the wall's dimension shows
    /// <paramref name="typed"/>.</summary>
    public static SKPoint EndFor(Shape wall, IReadOnlyList<SKPoint> points, double ux, double uy, double typed)
    {
        var n = points.Count;
        var begin = points[n - 2];
        var probe = points.Take(n - 1).ToList();
        probe.Add(new SKPoint((float)(begin.X + ux * typed), (float)(begin.Y + uy * typed)));
        var centre = typed + (typed - Measured(wall, probe, n - 2));
        return new SKPoint((float)(begin.X + ux * centre), (float)(begin.Y + uy * centre));
    }

    /// <summary>The wall before the one being drawn keeps its typed number: the corner between them moves along the previous wall until its
    /// dimension shows <paramref name="target"/> again (the new wall, heading (<paramref name="ux"/>, <paramref name="uy"/>), takes part of
    /// it). <paramref name="points"/> includes the moving end of the new wall.</summary>
    public static void KeepPreviousTyped(Shape wall, List<SKPoint> points, double target, double ux, double uy)
    {
        var n = points.Count;
        if (n < 3)
        {
            return;
        }

        for (var pass = 0; pass < 3; pass++)
        {
            var a = points[n - 3];
            var b = points[n - 2];
            double vx = b.X - a.X;
            double vy = b.Y - a.Y;
            var length = Math.Sqrt(vx * vx + vy * vy);
            if (length < 1e-6)
            {
                return;
            }

            vx /= length;
            vy /= length;
            var probe = points.Take(n - 1).ToList();
            probe.Add(new SKPoint((float)(b.X + ux * 50), (float)(b.Y + uy * 50)));
            var delta = target - Measured(wall, probe, n - 3);
            if (Math.Abs(delta) < 0.01)
            {
                return;
            }

            points[n - 2] = new SKPoint((float)(b.X + vx * delta), (float)(b.Y + vy * delta));
        }
    }

    /// <summary>A wall that ends free gives back what was added for a corner that never came: its last corner moves along it until the
    /// dimension of the last wall shows <paramref name="target"/>. <paramref name="points"/> has no moving end.</summary>
    public static void FitLast(Shape wall, List<SKPoint> points, double target)
    {
        var n = points.Count;
        for (var pass = 0; pass < 3 && n >= 2; pass++)
        {
            var a = points[n - 2];
            var b = points[n - 1];
            var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (length < 1e-6)
            {
                return;
            }

            var delta = target - Measured(wall, points, n - 2);
            if (Math.Abs(delta) < 0.01)
            {
                return;
            }

            points[n - 1] = new SKPoint((float)(b.X + (b.X - a.X) / length * delta), (float)(b.Y + (b.Y - a.Y) / length * delta));
        }
    }

    /// <summary>The typed wall ends close to the start of the room (the last of <paramref name="points"/> is its end): the room is closed. The
    /// last wall is lengthened for its second corner and the start of the first wall moves to where the last one now ends, so every wall
    /// keeps its typed inside length. Returns the new start (and end) point.</summary>
    public static SKPoint CloseRoom(Shape wall, IReadOnlyList<SKPoint> points, double typedLast)
    {
        var n = points.Count;
        var corner = points[n - 2];
        var end = points[n - 1];
        var length = Math.Sqrt((end.X - corner.X) * (end.X - corner.X) + (end.Y - corner.Y) * (end.Y - corner.Y));
        if (length < 1e-6)
        {
            return end;
        }

        var ux = (end.X - corner.X) / length;
        var uy = (end.Y - corner.Y) / length;
        var candidate = end;
        for (var pass = 0; pass < 4; pass++)
        {
            var polygon = points.Take(n - 1).ToList();
            polygon[0] = candidate;
            var delta = typedLast - Measured(wall, polygon, n - 2, closed: true);
            if (Math.Abs(delta) < 0.01)
            {
                break;
            }

            candidate = new SKPoint((float)(candidate.X + ux * delta), (float)(candidate.Y + uy * delta));
        }

        return candidate;
    }
}
