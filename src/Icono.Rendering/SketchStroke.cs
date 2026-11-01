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

    /// <summary>Draws <paramref name="path"/> with the colour, shader, blur and width of <paramref name="stroke"/>, in the sketch style. An end of the
    /// line that carries no head (<paramref name="taperStart"/>, <paramref name="taperEnd"/>) runs out thin, like the tail of a brush stroke.</summary>
    public static void Draw(SKCanvas canvas, SKPath path, SKPaint stroke, bool taperStart = false, bool taperEnd = false)
    {
        var amplitude = Math.Clamp(0.6f + stroke.StrokeWidth * 0.55f, 0.9f, 2.8f);
        using var rest = new SKPath();
        using var line = new SKPath();
        var hasLine = false;
        if (taperStart || taperEnd)
        {
            // the first open contour is the line itself (the heads follow as contours of their own)
            using var measure = new SKPathMeasure(path, false);
            var first = true;
            do
            {
                using var piece = new SKPath();
                if (measure.Length < 0.01f || !measure.GetSegment(0, measure.Length, piece, true))
                {
                    first = false;
                    continue;
                }

                if (first && !measure.IsClosed)
                {
                    line.AddPath(piece);
                    hasLine = true;
                }
                else
                {
                    rest.AddPath(piece);
                }

                first = false;
            }
            while (measure.NextContour());
        }

        for (var pass = 0; pass < 2; pass++)
        {
            if (!hasLine)
            {
                using var rough = Roughen(path, amplitude, pass);
                canvas.DrawPath(rough, stroke);
                continue;
            }

            using var roughRest = Roughen(rest, amplitude, pass);
            canvas.DrawPath(roughRest, stroke);
            using var roughLine = Roughen(line, amplitude, pass);
            DrawTapered(canvas, roughLine, stroke, taperStart, taperEnd, pass);
        }
    }

    /// <summary>The line as a filled outline whose width runs from nothing at a tapered end up to the full stroke width.</summary>
    private static void DrawTapered(SKCanvas canvas, SKPath line, SKPaint stroke, bool taperStart, bool taperEnd, int pass)
    {
        using var measure = new SKPathMeasure(line, false);
        var length = measure.Length;
        if (length < 1f)
        {
            return;
        }

        using var fill = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Fill, Color = stroke.Color, Shader = stroke.Shader, ImageFilter = stroke.ImageFilter,
        };

        var width = stroke.StrokeWidth;
        var count = Math.Max(4, (int)Math.Ceiling(length / 2f) + 1);
        var centre = new List<SKPoint>(count);
        var tangent = new List<SKPoint>(count);
        for (var i = 0; i < count; i++)
        {
            measure.GetPositionAndTangent(length * i / (count - 1), out var position, out var direction);
            centre.Add(position);
            tangent.Add(direction);
        }

        // one tapered end grows over most of the line, two tapered ends each over less than half of it
        var ramp = Math.Max(8f, length * (taperStart && taperEnd ? 0.45f : 0.7f));
        var left = new List<SKPoint>(count);
        var right = new List<SKPoint>(count);
        float lastHalf = 0;
        for (var i = 0; i < count; i++)
        {
            var s = length * i / (count - 1);
            var scale = 1f;
            if (taperStart)
            {
                scale = Math.Min(scale, Taper(s / ramp));
            }

            if (taperEnd)
            {
                scale = Math.Min(scale, Taper((length - s) / ramp));
            }

            // the pressure of the pen varies a little
            var half = width * scale * (1f + 0.07f * MathF.Sin(s * 0.09f + pass * 2f)) / 2f;
            var nx = -tangent[i].Y;
            var ny = tangent[i].X;
            left.Add(new SKPoint(centre[i].X + nx * half, centre[i].Y + ny * half));
            right.Add(new SKPoint(centre[i].X - nx * half, centre[i].Y - ny * half));
            lastHalf = half;
        }

        using var outline = new SKPath();
        outline.MoveTo(left[0]);
        for (var i = 1; i < left.Count; i++)
        {
            outline.LineTo(left[i]);
        }

        for (var i = right.Count - 1; i >= 0; i--)
        {
            outline.LineTo(right[i]);
        }

        outline.Close();
        canvas.DrawPath(outline, fill);

        // an end that is not tapered keeps its round cap
        if (!taperStart)
        {
            canvas.DrawCircle(centre[0].X, centre[0].Y, width / 2f, fill);
        }

        if (!taperEnd)
        {
            canvas.DrawCircle(centre[^1].X, centre[^1].Y, Math.Max(lastHalf, width / 2f), fill);
        }
    }

    /// <summary>From a point (0) up to the full width (1): a smooth, slightly convex rise, so the tail stays thin for a while and then swells.</summary>
    private static float Taper(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var smooth = t * t * (3 - 2 * t);
        return 0.03f + 0.97f * MathF.Pow(smooth, 0.85f);
    }

    /// <summary>The path as one stroke of the pen: every piece of it is drawn a little beside the true line. <paramref name="pass"/> is 0 for the first
    /// stroke and 1 for the one that goes over it again.</summary>
    internal static SKPath Roughen(SKPath path, float amplitude, int pass)
    {
        var result = new SKPath();
        using var iterator = path.CreateIterator(false);
        var p = new SKPoint[4];
        var piece = 0;
        var begin = true; // the next piece starts a new contour (a Move came before it); otherwise it carries on from the one before
        SKPathVerb verb;
        while ((verb = iterator.Next(p)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    begin = true;
                    break;
                case SKPathVerb.Line:
                    Line(result, p[0], p[1], amplitude, pass, piece++, begin);
                    begin = false;
                    break;
                case SKPathVerb.Quad:
                    Bezier(result, p[0], p[1], p[1], p[2], amplitude, pass, piece++, begin);
                    begin = false;
                    break;

                case SKPathVerb.Conic:
                {
                    using var curve = new SKPath();
                    curve.MoveTo(p[0]);
                    curve.ConicTo(p[1], p[2], iterator.ConicWeight());
                    Curve(result, curve, amplitude, pass, piece++, begin);
                    begin = false;
                    break;
                }

                case SKPathVerb.Cubic:
                    Bezier(result, p[0], p[1], p[2], p[3], amplitude, pass, piece++, begin);
                    begin = false;
                    break;
            }
        }

        return result;
    }

    /// <summary>A straight side: a slightly bowed stroke whose ends and middle are off the line by a little.</summary>
    private static void Line(SKPath result, SKPoint a, SKPoint b, float amplitude, int pass, int piece, bool begin)
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
        Begin(result, start, begin);
        result.CubicTo(c1, c2, end);
    }

    /// <summary>A curve given by its four control points: the two ends move by the same amount as the neighbouring pieces (they stay joined and keep
    /// running smoothly into each other), the two inner control points a little less. A quadratic curve is passed with its one control point twice.</summary>
    private static void Bezier(SKPath result, SKPoint a, SKPoint b, SKPoint c, SKPoint d, float amplitude, int pass, int piece, bool begin)
    {
        var length = Distance(a, b) + Distance(b, c) + Distance(c, d);
        var k = amplitude * Math.Min(1f, 0.1f + length / 60f);
        Begin(result, Off(a, k * 0.8f, Joint(piece), pass, 0), begin);
        result.CubicTo(Off(b, k * 0.5f, piece, pass, 2), Off(c, k * 0.5f, piece, pass, 3), Off(d, k * 0.8f, Joint(piece + 1), pass, 0));
    }

    /// <summary>A curved piece: points along it, each moved a little off the curve, joined by a smooth curve.</summary>
    private static void Curve(SKPath result, SKPath curve, float amplitude, int pass, int piece, bool begin)
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

        Begin(result, points[0], begin);
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

    /// <summary>Starts a contour at the point, or carries on to it from where the last piece ended (a gap of a fraction of a pixel at most).</summary>
    private static void Begin(SKPath result, SKPoint point, bool begin)
    {
        if (begin)
        {
            result.MoveTo(point);
        }
        else
        {
            result.LineTo(point);
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
