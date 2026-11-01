using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Draws the outline of a shape or a line like with a pen by hand, the way the sketch style of diagram tools does it: every side or curve is
/// drawn twice, each time a little off the true line, so the two strokes lie next to each other and cross like a quick drawing. The stroke keeps
/// its width, colour and ends; corners stay corners. The wobble depends only on the index of the piece, so the drawing stays calm while the shape
/// is moved.</summary>
public static class SketchStroke
{
    /// <summary>Distance between the control points of a drawn curve (px).</summary>
    private const float CurveStep = 26f;

    /// <summary>Draws <paramref name="path"/> with the colour, shader, blur and width of <paramref name="stroke"/>, in the sketch style.</summary>
    public static void Draw(SKCanvas canvas, SKPath path, SKPaint stroke)
    {
        var amplitude = Math.Clamp(0.6f + stroke.StrokeWidth * 0.55f, 0.9f, 2.8f);
        for (var pass = 0; pass < 2; pass++)
        {
            using var rough = Roughen(path, amplitude, pass);
            canvas.DrawPath(rough, stroke);
        }
    }

    /// <summary>The path as one stroke of the pen: every piece of it is drawn a little beside the true line. <paramref name="pass"/> is 0 for the first
    /// stroke and 1 for the one that goes over it again.</summary>
    internal static SKPath Roughen(SKPath path, float amplitude, int pass)
    {
        var result = new SKPath();
        using var iterator = path.CreateIterator(false);
        var p = new SKPoint[4];
        var piece = 0;
        SKPathVerb verb;
        while ((verb = iterator.Next(p)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Line:
                    Line(result, p[0], p[1], amplitude, pass, piece++);
                    break;
                case SKPathVerb.Quad:
                    Bezier(result, p[0], p[1], p[1], p[2], amplitude, pass, piece++);
                    break;

                case SKPathVerb.Conic:
                {
                    using var curve = new SKPath();
                    curve.MoveTo(p[0]);
                    curve.ConicTo(p[1], p[2], iterator.ConicWeight());
                    Curve(result, curve, amplitude, pass, piece++);
                    break;
                }

                case SKPathVerb.Cubic:
                    Bezier(result, p[0], p[1], p[2], p[3], amplitude, pass, piece++);
                    break;
            }
        }

        return result;
    }

    /// <summary>A straight side: a slightly bowed stroke whose ends and middle are off the line by a little.</summary>
    private static void Line(SKPath result, SKPoint a, SKPoint b, float amplitude, int pass, int piece)
    {
        var length = Distance(a, b);
        if (length < 0.01f)
        {
            return;
        }

        // short pieces (the wings of an arrow head) wobble less
        var k = amplitude * Math.Min(1f, 0.1f + length / 60f);
        // the end of one piece and the start of the next are the same point: they get the same offset, so the stroke runs on without a kink
        var start = Off(a, k * 0.8f, Joint(piece), pass, 0);
        var end = Off(b, k * 0.8f, Joint(piece + 1), pass, 0);
        var c1 = Off(new SKPoint(a.X + (b.X - a.X) / 3f, a.Y + (b.Y - a.Y) / 3f), k, piece, pass, 2);
        var c2 = Off(new SKPoint(a.X + (b.X - a.X) * 2f / 3f, a.Y + (b.Y - a.Y) * 2f / 3f), k, piece, pass, 3);
        result.MoveTo(start);
        result.CubicTo(c1, c2, end);
    }

    /// <summary>A curve given by its four control points: the two ends move by the same amount as the neighbouring pieces (they stay joined and keep
    /// running smoothly into each other), the two inner control points a little less. A quadratic curve is passed with its one control point twice.</summary>
    private static void Bezier(SKPath result, SKPoint a, SKPoint b, SKPoint c, SKPoint d, float amplitude, int pass, int piece)
    {
        var length = Distance(a, b) + Distance(b, c) + Distance(c, d);
        var k = amplitude * Math.Min(1f, 0.1f + length / 60f);
        result.MoveTo(Off(a, k * 0.8f, Joint(piece), pass, 0));
        result.CubicTo(Off(b, k * 0.5f, piece, pass, 2), Off(c, k * 0.5f, piece, pass, 3), Off(d, k * 0.8f, Joint(piece + 1), pass, 0));
    }

    /// <summary>A curved piece: points along it, each moved a little off the curve, joined by a smooth curve.</summary>
    private static void Curve(SKPath result, SKPath curve, float amplitude, int pass, int piece)
    {
        using var measure = new SKPathMeasure(curve, false);
        var length = measure.Length;
        if (length < 0.01f)
        {
            return;
        }

        var count = Math.Max(3, (int)Math.Ceiling(length / CurveStep) + 1);
        var k = amplitude * Math.Min(1f, 0.1f + length / 60f);
        var points = new List<SKPoint>(count);
        for (var i = 0; i < count; i++)
        {
            measure.GetPosition(length * i / (count - 1), out var position);
            points.Add(i == 0 ? Off(position, k * 0.8f, Joint(piece), pass, 0)
                : i == count - 1 ? Off(position, k * 0.8f, Joint(piece + 1), pass, 0)
                : Off(position, k * 0.3f, piece, pass, 10 + i));
        }

        // the points before the first and after the last one lie in the true direction of the curve, so the arc runs on smoothly into its neighbours
        measure.GetPositionAndTangent(0, out _, out var startTangent);
        measure.GetPositionAndTangent(length, out _, out var endTangent);
        var lead = Distance(points[0], points[1]);
        var tail = Distance(points[^2], points[^1]);
        var virtualBefore = new SKPoint(points[0].X - startTangent.X * lead, points[0].Y - startTangent.Y * lead);
        var virtualAfter = new SKPoint(points[^1].X + endTangent.X * tail, points[^1].Y + endTangent.Y * tail);

        result.MoveTo(points[0]);
        for (var i = 0; i < points.Count - 1; i++)
        {
            // Catmull-Rom through the moved points, as cubic pieces
            var before = i == 0 ? virtualBefore : points[i - 1];
            var after = i + 2 >= points.Count ? virtualAfter : points[i + 2];
            var from = points[i];
            var to = points[i + 1];
            result.CubicTo(
                new SKPoint(from.X + (to.X - before.X) / 6f, from.Y + (to.Y - before.Y) / 6f),
                new SKPoint(to.X - (after.X - from.X) / 6f, to.Y - (after.Y - from.Y) / 6f),
                to);
        }
    }

    /// <summary>The number of the point where piece <c>n</c> begins (the piece before it ends there).</summary>
    private static int Joint(int n) => 1000 + n;

    private static SKPoint Off(SKPoint p, float amount, int piece, int pass, int slot) =>
        new(p.X + Random(piece, pass, slot, 0) * amount, p.Y + Random(piece, pass, slot, 1) * amount);

    /// <summary>A number between -1 and 1 that is always the same for the same arguments (so the drawing does not flicker or change when redrawn).</summary>
    private static float Random(int a, int b, int c, int d)
    {
        unchecked
        {
            var h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791) ^ (uint)(d * 2654435761);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 32767.5f - 1f;
        }
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
