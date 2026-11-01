using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Draws the outline of a shape or a line like by hand. An open line becomes the stroke of a pen: it wobbles gently, its width breathes a
/// little and it starts thin and grows (like the arrow of a quick sketch). A closed outline is drawn twice with a slightly different wobble, like a
/// shape that was gone over again. The wobble depends only on the distance along the line, so the picture stays calm while the shape is moved.</summary>
public static class SketchStroke
{
    /// <summary>Distance between two samples of the line (px).</summary>
    private const float Step = 2.5f;

    /// <summary>Draws <paramref name="path"/> with the colour, shader, blur and width of <paramref name="stroke"/>, in the sketch style.</summary>
    public static void Draw(SKCanvas canvas, SKPath path, SKPaint stroke)
    {
        using var fill = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Fill, Color = stroke.Color, Shader = stroke.Shader, ImageFilter = stroke.ImageFilter,
        };

        using var measure = new SKPathMeasure(path, false);
        var contour = 0;
        do
        {
            var length = measure.Length;
            if (length < 0.5f)
            {
                continue;
            }

            var points = Sample(measure, length);
            if (measure.IsClosed)
            {
                for (var pass = 0; pass < 2; pass++)
                {
                    using var rough = Wobble(points, stroke.StrokeWidth, contour * 2 + pass, close: true);
                    canvas.DrawPath(rough, stroke);
                }
            }
            else
            {
                DrawPen(canvas, points, stroke.StrokeWidth, contour, fill);
            }

            contour++;
        }
        while (measure.NextContour());
    }

    private static List<SKPoint> Sample(SKPathMeasure measure, float length)
    {
        var count = Math.Max(2, (int)Math.Ceiling(length / Step) + 1);
        var points = new List<SKPoint>(count);
        for (var i = 0; i < count; i++)
        {
            measure.GetPosition(Math.Min(length, i * length / (count - 1)), out var position);
            points.Add(position);
        }

        return points;
    }

    private static float Amplitude(float width) => Math.Clamp(width * 0.35f + 0.5f, 0.6f, 2.2f);

    /// <summary>How far the pen is off the true line at distance <paramref name="s"/>: two slow waves that never repeat in step.</summary>
    private static float Offset(float s, float amplitude, int seed) =>
        amplitude * (0.6f * MathF.Sin(s * 0.045f + seed * 1.7f) + 0.4f * MathF.Sin(s * 0.113f + seed * 0.9f + 1f));

    private static SKPath Wobble(List<SKPoint> points, float width, int seed, bool close)
    {
        var path = new SKPath();
        var amplitude = Amplitude(width);
        var travelled = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                travelled += Distance(points[i - 1], points[i]);
            }

            var (nx, ny) = Normal(points, i);
            var offset = Offset(travelled, amplitude, seed);
            var p = new SKPoint(points[i].X + nx * offset, points[i].Y + ny * offset);
            if (i == 0)
            {
                path.MoveTo(p);
            }
            else
            {
                path.LineTo(p);
            }
        }

        if (close)
        {
            path.Close();
        }

        return path;
    }

    private static void DrawPen(SKCanvas canvas, List<SKPoint> points, float width, int seed, SKPaint fill)
    {
        var total = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            total += Distance(points[i - 1], points[i]);
        }

        // the small pieces (the wings of an arrow head) are just drawn, not tapered
        var small = total < 30f;
        var amplitude = Amplitude(width) * (small ? total / 30f : 1f);
        var left = new List<SKPoint>(points.Count);
        var right = new List<SKPoint>(points.Count);
        var radii = new List<float>(points.Count);
        var travelled = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                travelled += Distance(points[i - 1], points[i]);
            }

            var (nx, ny) = Normal(points, i);
            var offset = Offset(travelled, amplitude, seed);
            var half = width * (1f + 0.18f * MathF.Sin(travelled * 0.07f + seed)) * Taper(travelled, total, small) / 2f;
            var centre = new SKPoint(points[i].X + nx * offset, points[i].Y + ny * offset);
            left.Add(new SKPoint(centre.X + nx * half, centre.Y + ny * half));
            right.Add(new SKPoint(centre.X - nx * half, centre.Y - ny * half));
            radii.Add(half);
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

        // round ends
        var first = (left[0].X + right[0].X) / 2;
        var firstY = (left[0].Y + right[0].Y) / 2;
        var last = left.Count - 1;
        canvas.DrawCircle(first, firstY, radii[0], fill);
        canvas.DrawCircle((left[last].X + right[last].X) / 2, (left[last].Y + right[last].Y) / 2, radii[last], fill);
    }

    /// <summary>The pen presses lightly at the start (about half the width) and grows to full width; the very end is nearly full.</summary>
    private static float Taper(float s, float total, bool small)
    {
        if (small)
        {
            return 1f;
        }

        var rise = Smooth(Math.Min(1f, s / Math.Min(40f, total * 0.3f)));
        var fall = Smooth(Math.Min(1f, (total - s) / Math.Min(14f, total * 0.12f)));
        return (0.45f + 0.55f * rise) * (0.85f + 0.15f * fall);
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    private static (float X, float Y) Normal(List<SKPoint> points, int i)
    {
        var a = points[Math.Max(0, i - 1)];
        var b = points[Math.Min(points.Count - 1, i + 1)];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        return length < 1e-4f ? (0, 0) : (-dy / length, dx / length);
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
