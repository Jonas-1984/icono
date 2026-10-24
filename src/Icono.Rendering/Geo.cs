using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Small 2D helpers shared by the geometry code (snapping, outlines, measuring).</summary>
public static class Geo
{
    public static float Distance(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The point of the segment a-b that is closest to <paramref name="p"/>.</summary>
    public static SKPoint ClosestOnSegment(SKPoint a, SKPoint b, SKPoint p)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9f)
        {
            return a;
        }

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0f, 1f);
        return new SKPoint(a.X + t * dx, a.Y + t * dy);
    }

    /// <summary>Direction of the way from <paramref name="from"/> to <paramref name="to"/> in degrees, 0 = right, counter-clockwise as the
    /// eye sees it (the screen's y axis points down), 0 to 360.</summary>
    public static float AngleDegrees(SKPoint from, SKPoint to) =>
        (-MathF.Atan2(to.Y - from.Y, to.X - from.X) * 180f / MathF.PI + 360f) % 360f;
}
