using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>What a drawn stroke turned out to be.</summary>
public enum RecognizedKind
{
    Line,
    Rectangle,
    Ellipse,
    Polygon,
}

/// <summary>A clean shape in the place of a rough stroke. <see cref="Points"/> are the end points of a line or the corners of a polygon;
/// <see cref="Center"/>, <see cref="Width"/>, <see cref="Height"/> and <see cref="Angle"/> (degrees) describe a rectangle or an ellipse.</summary>
public sealed record RecognizedStroke(
    RecognizedKind Kind, IReadOnlyList<SKPoint> Points, SKPoint Center, double Width, double Height, double Angle);

/// <summary>Draw and hold: a rough circle, line, rectangle or triangle drawn with the pen is turned into the clean shape (the hand sign of the pen
/// for "make it straight"). The recognizer only looks at the points of the stroke - shared by every app; <see cref="StrokeShapes"/> makes the object.</summary>
public static class StrokeShapeRecognizer
{
    /// <summary>The clean shape for the stroke, or null when it does not look like one (a handwritten word, a scribble, too small).</summary>
    public static RecognizedStroke? Recognize(IReadOnlyList<SKPoint> raw)
    {
        if (raw.Count < 6)
        {
            return null;
        }

        var left = raw.Min(p => p.X);
        var right = raw.Max(p => p.X);
        var top = raw.Min(p => p.Y);
        var bottom = raw.Max(p => p.Y);
        var diagonal = MathF.Sqrt((right - left) * (right - left) + (bottom - top) * (bottom - top));
        var length = Length(raw);
        if (diagonal < 12 || length < 20)
        {
            return null;
        }

        var chord = Distance(raw[0], raw[^1]);

        // a line: the stroke runs (nearly) straight from one end to the other
        if (chord > 0.82 * length && Deviation(raw) <= 0.07 * chord)
        {
            return Line(raw[0], raw[^1]);
        }

        // a closed shape: the end comes back to the start
        if (chord > 0.2 * length)
        {
            return null;
        }

        var corners = Corners(raw, diagonal);
        if (corners.Count == 3)
        {
            return new RecognizedStroke(RecognizedKind.Polygon, corners, Mean(corners), 0, 0, 0);
        }

        if (corners.Count == 4)
        {
            return RectangleOrQuad(corners);
        }

        return corners.Count >= 5 ? Ellipse(raw, left, right, top, bottom) : null;
    }

    private static RecognizedStroke Line(SKPoint a, SKPoint b)
    {
        // a line that is almost horizontal or vertical becomes exactly so
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var angle = Math.Atan2(Math.Abs(dy), Math.Abs(dx)) * 180 / Math.PI;
        if (angle < 5)
        {
            var y = (a.Y + b.Y) / 2;
            a = new SKPoint(a.X, y);
            b = new SKPoint(b.X, y);
        }
        else if (angle > 85)
        {
            var x = (a.X + b.X) / 2;
            a = new SKPoint(x, a.Y);
            b = new SKPoint(x, b.Y);
        }

        return new RecognizedStroke(RecognizedKind.Line, [a, b], Mean([a, b]), 0, 0, 0);
    }

    /// <summary>The corners of a closed stroke: the stroke made coarse, nearly straight bends and corners that lie close together dropped.</summary>
    private static List<SKPoint> Corners(IReadOnlyList<SKPoint> raw, float diagonal)
    {
        var points = FreehandBuilder.Simplify(raw, Math.Max(2, 0.05f * diagonal));
        if (points.Count > 1 && Distance(points[0], points[^1]) < 0.15f * diagonal)
        {
            points.RemoveAt(points.Count - 1); // the closing point is the start again
        }

        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < points.Count && points.Count > 2; i++)
            {
                var previous = points[(i + points.Count - 1) % points.Count];
                var next = points[(i + 1) % points.Count];
                var here = points[i];
                var close = Distance(here, next) < 0.12f * diagonal;
                if (close)
                {
                    points[i] = new SKPoint((here.X + next.X) / 2, (here.Y + next.Y) / 2);
                    points.RemoveAt((i + 1) % points.Count);
                    changed = true;
                    break;
                }

                if (Turn(previous, here, next) < 28)
                {
                    points.RemoveAt(i); // hardly a bend: the start of the stroke lay in the middle of a side
                    changed = true;
                    break;
                }
            }
        }
        while (changed);

        return points;
    }

    private static RecognizedStroke RectangleOrQuad(List<SKPoint> c)
    {
        // a rectangle: four right angles, opposite sides of the same length
        var right = true;
        for (var i = 0; i < 4; i++)
        {
            right &= Math.Abs(Turn(c[(i + 3) % 4], c[i], c[(i + 1) % 4]) - 90) < 24;
        }

        var side0 = Distance(c[0], c[1]);
        var side1 = Distance(c[1], c[2]);
        var side2 = Distance(c[2], c[3]);
        var side3 = Distance(c[3], c[0]);
        right &= Math.Abs(side0 - side2) < 0.28 * Math.Max(side0, side2) && Math.Abs(side1 - side3) < 0.28 * Math.Max(side1, side3);
        if (!right)
        {
            return new RecognizedStroke(RecognizedKind.Polygon, c, Mean(c), 0, 0, 0);
        }

        // the direction of the first pair of opposite sides (the two directions added, so that the average does not jump at the turn of the angle)
        var d0 = Direction(c[0], c[1]);
        var d1 = Direction(c[3], c[2]);
        var degrees = Math.Atan2(d0.Y + d1.Y, d0.X + d1.X) * 180 / Math.PI;
        var width = (side0 + side2) / 2;
        var height = (side1 + side3) / 2;

        // a box turned by less than 45 degrees: the sides are swapped when the first pair is the upright one
        degrees = ((degrees % 180) + 180) % 180;
        if (degrees > 90)
        {
            degrees -= 180;
        }

        if (degrees > 45)
        {
            degrees -= 90;
            (width, height) = (height, width);
        }
        else if (degrees <= -45)
        {
            degrees += 90;
            (width, height) = (height, width);
        }

        // nearly upright: exactly upright
        if (Math.Abs(degrees) < 5)
        {
            degrees = 0;
        }

        return new RecognizedStroke(RecognizedKind.Rectangle, c, Mean(c), width, height, degrees);
    }

    private static RecognizedStroke? Ellipse(IReadOnlyList<SKPoint> raw, float left, float right, float top, float bottom)
    {
        var a = (right - left) / 2;
        var b = (bottom - top) / 2;
        if (a < 6 || b < 6)
        {
            return null;
        }

        var centre = new SKPoint((left + right) / 2, (top + bottom) / 2);
        double sum = 0;
        double worst = 0;
        foreach (var p in raw)
        {
            var r = Math.Sqrt(Math.Pow((p.X - centre.X) / a, 2) + Math.Pow((p.Y - centre.Y) / b, 2));
            var off = Math.Abs(r - 1);
            sum += off;
            worst = Math.Max(worst, off);
        }

        // the stroke runs close to the ellipse of its frame the whole way round
        if (sum / raw.Count > 0.13 || worst > 0.42)
        {
            return null;
        }

        var width = 2.0 * a;
        var height = 2.0 * b;
        if (Math.Abs(width - height) < 0.14 * Math.Max(width, height))
        {
            width = height = (width + height) / 2; // a circle
        }

        return new RecognizedStroke(RecognizedKind.Ellipse, [], centre, width, height, 0);
    }

    // ---------------------------------------------------------------- geometry

    private static float Distance(SKPoint a, SKPoint b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static float Length(IReadOnlyList<SKPoint> points)
    {
        float sum = 0;
        for (var i = 1; i < points.Count; i++)
        {
            sum += Distance(points[i - 1], points[i]);
        }

        return sum;
    }

    /// <summary>The largest distance of a point of the stroke from the straight line between its ends.</summary>
    private static float Deviation(IReadOnlyList<SKPoint> points)
    {
        var a = points[0];
        var b = points[^1];
        var length = Distance(a, b);
        if (length < 1e-3f)
        {
            return float.MaxValue;
        }

        return points.Max(p => MathF.Abs((b.X - a.X) * (a.Y - p.Y) - (a.X - p.X) * (b.Y - a.Y)) / length);
    }

    private static SKPoint Mean(IReadOnlyList<SKPoint> points) => new(points.Average(p => p.X), points.Average(p => p.Y));

    private static SKPoint Direction(SKPoint from, SKPoint to)
    {
        var length = Distance(from, to);
        return length < 1e-6f ? new SKPoint(1, 0) : new SKPoint((to.X - from.X) / length, (to.Y - from.Y) / length);
    }

    /// <summary>The angle between the two sides at <paramref name="b"/> in degrees (a right angle is 90, a straight line 180)... given as the
    /// bend: how far the path turns at b (0 = straight on, 90 = a right angle).</summary>
    private static double Turn(SKPoint a, SKPoint b, SKPoint c)
    {
        var d1 = Direction(a, b);
        var d2 = Direction(b, c);
        var dot = Math.Clamp(d1.X * d2.X + d1.Y * d2.Y, -1, 1);
        return Math.Acos(dot) * 180 / Math.PI;
    }
}
