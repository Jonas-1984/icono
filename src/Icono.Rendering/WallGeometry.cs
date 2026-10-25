using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Geometry of a <see cref="ShapeKind.Wall"/>: the centre line through its points and the thick band around it.</summary>
public static class WallGeometry
{
    /// <summary>The wall's points in document space.</summary>
    public static List<SKPoint> AbsolutePoints(Shape wall)
    {
        var result = new List<SKPoint>();
        if (wall.Points is null)
        {
            return result;
        }

        foreach (var p in wall.Points)
        {
            result.Add(new SKPoint((float)(wall.X + p.X), (float)(wall.Y + p.Y)));
        }

        return result;
    }

    /// <summary>The wall's centre line (closed when the wall is a ring).</summary>
    public static SKPath BuildCenterLine(Shape wall)
    {
        var path = new SKPath();
        var points = AbsolutePoints(wall);
        if (points.Count == 0)
        {
            return path;
        }

        path.MoveTo(points[0]);
        for (var i = 1; i < points.Count; i++)
        {
            path.LineTo(points[i]);
        }

        if (wall.WallClosed && points.Count >= 3)
        {
            path.Close();
        }

        return path;
    }

    /// <summary>The filled band of the wall (centre line stroked with the wall thickness, square ends, mitred corners).
    /// A closed wall yields a ring: outer and inner outline.</summary>
    public static SKPath BuildOutline(Shape wall)
    {
        using var center = BuildCenterLine(wall);
        var outline = new SKPath();
        if (center.PointCount < 2)
        {
            return outline;
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(0.5, wall.WallThickness),
            StrokeJoin = SKStrokeJoin.Miter,
            StrokeMiter = 8f,
            StrokeCap = SKStrokeCap.Butt,
        };

        paint.GetFillPath(center, outline);

        // The stroker leaves every corner as overlapping pieces (the inner side of a bend crosses itself), which showed up as lines
        // running through the joint. Merging them into one outline makes the walls a single unit with clean mitred corners.
        outline.Simplify();

        if (wall.Rotation != 0)
        {
            outline.Transform(SKMatrix.CreateRotationDegrees(
                (float)wall.Rotation, (float)(wall.X + wall.Width / 2), (float)(wall.Y + wall.Height / 2)));
        }

        return outline;
    }

    /// <summary>The segments of the centre line as point pairs (including the closing one of a ring).</summary>
    public static List<(SKPoint From, SKPoint To)> Segments(Shape wall)
    {
        var points = AbsolutePoints(wall);
        var segments = new List<(SKPoint, SKPoint)>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            segments.Add((points[i], points[i + 1]));
        }

        if (wall.WallClosed && points.Count >= 3)
        {
            segments.Add((points[^1], points[0]));
        }

        return segments;
    }

    /// <summary>The two ends of the wall face of <paramref name="segment"/> on one side (1 = left of the walking direction, -1 = right): the
    /// points where this face meets the faces of the neighbouring segments (a mitred corner), or the plain offset of the end of an open
    /// wall. This is what a dimension of the inside or the outside of a wall measures.</summary>
    public static (SKPoint From, SKPoint To) FacePoints(Shape wall, int segment, float side)
    {
        var points = AbsolutePoints(wall);
        var n = points.Count;
        var closed = wall.WallClosed && n >= 3;
        var segmentCount = closed ? n : n - 1;
        var half = (float)Math.Max(0.25, wall.WallThickness) / 2;

        SKPoint? Normal(int index)
        {
            var a = points[index];
            var b = points[(index + 1) % n];
            var length = Geo.Distance(a, b);
            return length < 1e-4f ? null : new SKPoint(-(b.Y - a.Y) / length * side, (b.X - a.X) / length * side);
        }

        SKPoint Corner(int vertex, SKPoint own, int? otherSegment)
        {
            var offset = own;
            if (otherSegment is { } other && Normal(other) is { } next)
            {
                var denominator = 1 + own.X * next.X + own.Y * next.Y;
                // nearly turning back on itself: a mitre would be endless, so the plain offset is used
                if (denominator > 0.03f)
                {
                    offset = new SKPoint((own.X + next.X) / denominator, (own.Y + next.Y) / denominator);
                }
            }

            return new SKPoint(points[vertex].X + offset.X * half, points[vertex].Y + offset.Y * half);
        }

        if (segment < 0 || segment >= segmentCount || Normal(segment) is not { } normal)
        {
            return (points[Math.Clamp(segment, 0, n - 1)], points[Math.Clamp(segment + 1, 0, n - 1)]);
        }

        int? before = closed ? (segment - 1 + n) % n : segment > 0 ? segment - 1 : null;
        int? after = closed ? (segment + 1) % n : segment + 1 < segmentCount ? segment + 1 : null;
        return (Corner(segment, normal, before), Corner((segment + 1) % n, normal, after));
    }

    /// <summary>The length (page px) a dimension of this segment shows, following the wall's <see cref="Shape.WallDimensionMeasure"/>: the face on
    /// the dimension's side (inside or outside) or the centre line.</summary>
    public static double MeasuredLength(Shape wall, int segment)
    {
        var segments = Segments(wall);
        if (segment < 0 || segment >= segments.Count)
        {
            return 0;
        }

        if (wall.WallDimensionMeasure == WallMeasure.Centre)
        {
            return Geo.Distance(segments[segment].From, segments[segment].To);
        }

        var (from, to) = FacePoints(wall, segment, WallRenderer.FaceSide(wall, WallRenderer.LineSide(wall)));
        return Geo.Distance(from, to);
    }

    /// <summary>Reads a typed real-world length such as "250", "2.5 m", "2500mm" or "98 in" (a number alone is in
    /// <paramref name="defaultUnit"/>; a decimal comma is fine) and returns it as page px at the drawing scale. False for anything
    /// that is not a positive length.</summary>
    public static bool TryParseLength(string? text, LengthUnit defaultUnit, double scale, out double px)
    {
        px = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim().Replace(',', '.');
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.'))
        {
            end++;
        }

        if (end == 0 || !double.TryParse(s[..end], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        var suffix = s[end..].Trim().ToLowerInvariant();
        var unit = suffix switch
        {
            "" => defaultUnit,
            "mm" => LengthUnit.Millimeter,
            "cm" => LengthUnit.Centimeter,
            "m" => LengthUnit.Meter,
            "in" or "\"" or "inch" or "inches" => LengthUnit.Inch,
            _ => (LengthUnit?)null,
        };
        if (unit is not { } known)
        {
            return false;
        }

        var inches = known switch
        {
            LengthUnit.Millimeter => value / 25.4,
            LengthUnit.Meter => value / 0.0254,
            LengthUnit.Inch => value,
            _ => value / 2.54,
        };
        px = inches * 96.0 / (scale > 0 ? scale : 1);
        return true;
    }

    /// <summary>Writes a length (px at 96 DPI) in the given unit, e.g. "12.5 cm".</summary>
    public static string FormatLength(double px, LengthUnit unit, double? scale = null)
    {
        var inches = px * (scale ?? DrawingScale.Denominator) / 96.0;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return unit switch
        {
            LengthUnit.Millimeter => (inches * 25.4).ToString("0.#", culture) + " mm",
            LengthUnit.Meter => (inches * 0.0254).ToString("0.###", culture) + " m",
            LengthUnit.Inch => inches.ToString("0.##", culture) + " in",
            _ => (inches * 2.54).ToString("0.##", culture) + " cm",
        };
    }
}
