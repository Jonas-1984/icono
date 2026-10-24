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

    /// <summary>Writes a length (px at 96 DPI) in the given unit, e.g. "12.5 cm".</summary>
    public static string FormatLength(double px, LengthUnit unit)
    {
        var inches = px / 96.0;
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
