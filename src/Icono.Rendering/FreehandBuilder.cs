using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>The Handwriting tool: the points a pen, a finger or the mouse went through become one smooth stroke. The stroke is kept as a path
/// (<see cref="Shape.CustomPathData"/>, stroke only) so it can be moved, resized, recoloured and deleted like any other shape.</summary>
public static class FreehandBuilder
{
    /// <summary>Takes out the points that lie on the line between their neighbours (Ramer-Douglas-Peucker): the pen delivers far more points than
    /// the shape of the stroke needs, and fewer points make a smoother, smaller stroke.</summary>
    public static List<SKPoint> Simplify(IReadOnlyList<SKPoint> points, float tolerance)
    {
        if (points.Count < 3)
        {
            return points.ToList();
        }

        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int From, int To)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            var farthest = -1;
            var distance = tolerance;
            for (var i = from + 1; i < to; i++)
            {
                var d = Geo.Distance(points[i], Geo.ClosestOnSegment(points[from], points[to], points[i]));
                if (d > distance)
                {
                    distance = d;
                    farthest = i;
                }
            }

            if (farthest >= 0)
            {
                keep[farthest] = true;
                stack.Push((from, farthest));
                stack.Push((farthest, to));
            }
        }

        return points.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>Takes the tremor out of a stroke: the points are laid at an even distance along the stroke and each one is replaced by the weighted
    /// average of its neighbours (a Gaussian of <paramref name="sigma"/> page px), so a shaking hand draws the line it meant to draw. The two ends stay
    /// exactly where the pen went down and was lifted. Strokes that are short or have no sigma are returned as they are.</summary>
    public static List<SKPoint> Stabilize(IReadOnlyList<SKPoint> points, float sigma)
    {
        if (sigma <= 0 || points.Count < 3)
        {
            return points.ToList();
        }

        var step = Math.Max(sigma / 2, 0.05f);
        var samples = Resample(points, step);
        var count = samples.Count;
        if (count < 3)
        {
            return samples;
        }

        var radius = (int)MathF.Ceiling(3 * sigma / step);
        var weights = new float[2 * radius + 1];
        for (var k = -radius; k <= radius; k++)
        {
            var distance = k * step / sigma;
            weights[k + radius] = MathF.Exp(-0.5f * distance * distance);
        }

        var result = new List<SKPoint>(count) { samples[0] };
        for (var i = 1; i < count - 1; i++)
        {
            float x = 0, y = 0, sum = 0;
            for (var k = -radius; k <= radius; k++)
            {
                var p = At(samples, i + k);
                var w = weights[k + radius];
                x += p.X * w;
                y += p.Y * w;
                sum += w;
            }

            result.Add(new SKPoint(x / sum, y / sum));
        }

        result.Add(samples[^1]);
        return result;
    }

    /// <summary>The point at <paramref name="index"/>, where an index beyond an end is the mirror image of the point on the other side of that end: the
    /// stroke goes on in the same direction there, so averaging does not pull the ends inwards.</summary>
    private static SKPoint At(List<SKPoint> samples, int index)
    {
        var last = samples.Count - 1;
        if (index < 0)
        {
            var mirror = samples[Math.Min(-index, last)];
            return new SKPoint(2 * samples[0].X - mirror.X, 2 * samples[0].Y - mirror.Y);
        }

        if (index > last)
        {
            var mirror = samples[Math.Max(2 * last - index, 0)];
            return new SKPoint(2 * samples[last].X - mirror.X, 2 * samples[last].Y - mirror.Y);
        }

        return samples[index];
    }

    /// <summary>The stroke again with a point every <paramref name="step"/> along it (the last point of the stroke is always kept).</summary>
    private static List<SKPoint> Resample(IReadOnlyList<SKPoint> points, float step)
    {
        var result = new List<SKPoint> { points[0] };
        var travelled = 0f;
        var next = step;
        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var length = Geo.Distance(from, to);
            if (length <= 0)
            {
                continue;
            }

            while (travelled + length >= next)
            {
                var t = (next - travelled) / length;
                result.Add(new SKPoint(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t));
                next += step;
            }

            travelled += length;
        }

        var end = points[^1];
        if (result.Count > 1 && Geo.Distance(result[^1], end) < step * 0.5f)
        {
            result[^1] = end;
        }
        else
        {
            result.Add(end);
        }

        return result;
    }

    /// <summary>The smooth stroke through the points: every point is passed with the direction of the stroke there (from its two neighbours), so no
    /// corner shows however the pen moved, and the curve stays where the points are even when they lie far apart.</summary>
    public static SKPath Smooth(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath();
        AppendSmooth(path, points, startNew: true);
        return path;
    }

    /// <summary>Turns at which the stroke is a SHARP CORNER and not a curve - the tip of an arrow, the corner of a Z or an N. Returns the indices of
    /// those points of <paramref name="points"/> (never the first or the last). A corner is where the direction changes by 60 degrees or more
    /// within <paramref name="window"/> (a page distance) on either side AND that turn happens at the point itself: half the window already shows
    /// most of it. A round curve spreads its turn along the stroke (half the window shows about half of it), so a small "o" is no corner however
    /// tight it is.</summary>
    public static List<int> DetectCorners(IReadOnlyList<SKPoint> points, float window)
    {
        var corners = new List<int>();
        var count = points.Count;
        if (window <= 0 || count < 3)
        {
            return corners;
        }

        // the length along the stroke at every point
        var along = new float[count];
        for (var i = 1; i < count; i++)
        {
            along[i] = along[i - 1] + Geo.Distance(points[i - 1], points[i]);
        }

        var total = along[^1];
        if (total < 2 * window)
        {
            return corners;
        }

        SKPoint At(float length)
        {
            var index = Array.BinarySearch(along, length);
            if (index >= 0)
            {
                return points[index];
            }

            var after = ~index; // first point beyond the length
            var span = along[after] - along[after - 1];
            var t = span > 0 ? (length - along[after - 1]) / span : 0;
            return new SKPoint(
                points[after - 1].X + (points[after].X - points[after - 1].X) * t,
                points[after - 1].Y + (points[after].Y - points[after - 1].Y) * t);
        }

        // how far the direction turns at point i when looking <reach> back and forward along the stroke (radians, 0 = straight on)
        float Turn(int i, float reach)
        {
            if (along[i] - reach < 0 || along[i] + reach > total)
            {
                return 0;
            }

            var back = At(along[i] - reach);
            var ahead = At(along[i] + reach);
            float inX = points[i].X - back.X, inY = points[i].Y - back.Y;
            float outX = ahead.X - points[i].X, outY = ahead.Y - points[i].Y;
            return MathF.Atan2(MathF.Abs(inX * outY - inY * outX), inX * outX + inY * outY);
        }

        const float MinimumTurn = 60 * MathF.PI / 180;
        const float AtThePoint = 0.7f;
        const float AtLeastHalfWay = 45 * MathF.PI / 180;

        var candidates = new List<(int Index, float Turn)>();
        for (var i = 1; i < count - 1; i++)
        {
            var wide = Turn(i, window);
            if (wide >= MinimumTurn)
            {
                var near = Turn(i, window / 2);
                if (near >= AtLeastHalfWay && near >= wide * AtThePoint)
                {
                    candidates.Add((i, wide));
                }
            }
        }

        // of several candidates close together (the pen lingers at a tip) only the sharpest one is a corner
        foreach (var candidate in candidates.OrderByDescending(c => c.Turn))
        {
            if (corners.All(c => MathF.Abs(along[c] - along[candidate.Index]) > window))
            {
                corners.Add(candidate.Index);
            }
        }

        corners.Sort();
        return corners;
    }

    /// <summary>The smooth stroke through the points (<see cref="Smooth"/>) added to <paramref name="path"/>: with <paramref name="startNew"/> it begins
    /// there, otherwise it goes on from where the path is (the first point is where the path ends).</summary>
    private static void AppendSmooth(SKPath path, IReadOnlyList<SKPoint> points, bool startNew)
    {
        if (points.Count == 0)
        {
            return;
        }

        if (startNew)
        {
            path.MoveTo(points[0]);
        }

        if (points.Count == 1)
        {
            path.LineTo(points[0].X + 0.1f, points[0].Y); // a dot (the round cap makes it a dot)
            return;
        }

        if (points.Count == 2)
        {
            path.LineTo(points[1]);
            return;
        }

        var last = points.Count - 1;
        var directions = new SKPoint[points.Count];
        for (var i = 0; i <= last; i++)
        {
            var before = points[Math.Max(i - 1, 0)];
            var after = points[Math.Min(i + 1, last)];
            var dx = after.X - before.X;
            var dy = after.Y - before.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            directions[i] = length > 0 ? new SKPoint(dx / length, dy / length) : default;
        }

        for (var i = 0; i < last; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var reach = Geo.Distance(from, to) / 3;
            path.CubicTo(
                from.X + directions[i].X * reach, from.Y + directions[i].Y * reach,
                to.X - directions[i + 1].X * reach, to.Y - directions[i + 1].Y * reach,
                to.X, to.Y);
        }
    }

    /// <summary>The path of a stroke: smoothed everywhere except at its sharp corners (<see cref="DetectCorners"/>). The stroke is cut at every corner and
    /// each piece is stabilised and smoothed on its own - a piece keeps its two ends exactly where they are - so a corner stays a corner: the tip of an
    /// arrow is a point and not a rounded bend. <paramref name="keepCorners"/> false smooths everything, corners included.</summary>
    public static SKPath BuildPath(IReadOnlyList<SKPoint> raw, float tolerance, float smoothing, bool keepCorners = true)
    {
        var path = new SKPath();
        if (raw.Count == 0)
        {
            return path;
        }

        // the corner window follows the same screen-based sizes as the tolerance and the smoothing: about ten screen pixels
        var corners = keepCorners ? DetectCorners(raw, Math.Max(tolerance * 12, smoothing * 3)) : [];
        var from = 0;
        foreach (var corner in corners.Append(raw.Count - 1))
        {
            var piece = new List<SKPoint>(corner - from + 1);
            for (var i = from; i <= corner; i++)
            {
                piece.Add(raw[i]);
            }

            AppendSmooth(path, Simplify(Stabilize(piece, smoothing), tolerance), startNew: from == 0);
            from = corner;
        }

        return path;
    }

    /// <summary>Makes the shape of a finished stroke, or null when there is no stroke (no points). <paramref name="tolerance"/> is the page distance a point
    /// may be off the simplified stroke; <paramref name="smoothing"/> is the page distance over which the tremor of the hand is averaged out
    /// (see <see cref="Stabilize"/>, 0 = the stroke as the pen drew it); sharp corners stay sharp (see <see cref="BuildPath"/>).</summary>
    public static Shape? CreateShape(IReadOnlyList<SKPoint> raw, ArgbColor color, double width, float tolerance, float smoothing = 0)
    {
        if (raw.Count == 0)
        {
            return null;
        }

        using var smooth = BuildPath(raw, tolerance, smoothing);
        var bounds = smooth.TightBounds;
        var frameWidth = Math.Max(1f, bounds.Width);
        var frameHeight = Math.Max(1f, bounds.Height);

        // the path is stored in the 24 x 24 box every path shape is scaled from
        using var local = new SKPath(smooth);
        local.Transform(SKMatrix.CreateScaleTranslation(24f / frameWidth, 24f / frameHeight, -bounds.Left * 24f / frameWidth, -bounds.Top * 24f / frameHeight));

        return new Shape
        {
            Kind = ShapeKind.CombinedPath,
            Name = "Handwriting",
            X = bounds.Left,
            Y = bounds.Top,
            Width = frameWidth,
            Height = frameHeight,
            CustomPathData = local.ToSvgPathData(),
            CustomPathStrokeOnly = true,
            FillEnabled = false,
            Freehand = true,
            BorderColor = color,
            BorderThickness = width,
        };
    }

    /// <summary>The sensitivity the pen starts with: a light touch is 0.3 times the pen width, a hard one 1.7 times.</summary>
    public const double DefaultPressureStrength = 0.7;

    /// <summary>How thick a stroke is at a pen pressure (0 - 1) compared to the pen width: the width is that of the pen at half pressure (a touch with
    /// no pressure information is half pressure). <paramref name="strength"/> (0 - 1) is how much the pressure changes it: 0 = always the same width, 0.7 = from
    /// 0.3 times (light) to 1.7 times (hard), 1 = from a hairline to twice the width.</summary>
    public static double PressureFactor(double pressure, double strength = DefaultPressureStrength) =>
        Math.Max(0.05, 1 + (Math.Clamp(pressure, 0, 1) - 0.5) * 2 * Math.Clamp(strength, 0, 1));

    /// <summary>A stroke written with pen pressure: a filled outline whose width follows the pressure at every point, with round ends (<see cref="Shape.CustomPathData"/>,
    /// filled) - and the line down its middle (<see cref="Shape.InkCenterline"/>), which is what the writing is read from. <paramref name="pressure"/> has one value
    /// (0 - 1) for every one of <paramref name="raw"/>; <paramref name="width"/> is the pen width at half pressure.</summary>
    public static Shape? CreatePressureShape(
        IReadOnlyList<SKPoint> raw, IReadOnlyList<float> pressure, ArgbColor color, double width, float tolerance, float smoothing = 0,
        double strength = DefaultPressureStrength)
    {
        if (raw.Count == 0)
        {
            return null;
        }

        using var centre = BuildPath(raw, tolerance, smoothing);
        using var measure = new SKPathMeasure(centre, false);
        var length = measure.Length;

        // the pressure along the stroke, by the distance the pen had gone (the smoothed stroke is shorter than the raw points but about as long)
        var cumulative = new float[raw.Count];
        for (var k = 1; k < raw.Count; k++)
        {
            cumulative[k] = cumulative[k - 1] + Geo.Distance(raw[k - 1], raw[k]);
        }

        float PressureAt(float fraction)
        {
            if (cumulative[^1] < 1e-3f || pressure.Count < raw.Count)
            {
                return pressure.Count > 0 ? pressure[0] : 0.5f;
            }

            var target = fraction * cumulative[^1];
            var index = Array.BinarySearch(cumulative, target);
            if (index < 0)
            {
                index = ~index;
            }

            index = Math.Clamp(index, 1, raw.Count - 1);
            var span = cumulative[index] - cumulative[index - 1];
            var t = span < 1e-6f ? 0f : (target - cumulative[index - 1]) / span;
            return pressure[index - 1] + (pressure[index] - pressure[index - 1]) * Math.Clamp(t, 0f, 1f);
        }

        using var outline = new SKPath();
        if (length < 0.5f)
        {
            // a dot: a round dot as thick as the pressure says
            var dot = (float)(width * PressureFactor(pressure.Count > 0 ? pressure.Average() : 0.5, strength)) / 2;
            outline.AddCircle(raw[0].X, raw[0].Y, Math.Max(0.4f, dot));
            return OutlineShape(outline, null, color);
        }

        var count = Math.Max(2, (int)Math.Ceiling(length / 1.5f));
        var centres = new SKPoint[count + 1];
        var normals = new SKPoint[count + 1];
        var tangents = new SKPoint[count + 1];
        var widths = new float[count + 1];
        for (var k = 0; k <= count; k++)
        {
            measure.GetPositionAndTangent(length * k / count, out var p, out var t);
            centres[k] = p;
            tangents[k] = t;
            normals[k] = new SKPoint(-t.Y, t.X);
            widths[k] = (float)(width * PressureFactor(PressureAt((float)k / count), strength));
        }

        // the width changes smoothly: a light average, twice
        for (var pass = 0; pass < 2; pass++)
        {
            var copy = (float[])widths.Clone();
            for (var k = 0; k <= count; k++)
            {
                float sum = 0;
                var n = 0;
                for (var d = -3; d <= 3; d++)
                {
                    var j = k + d;
                    if (j >= 0 && j <= count)
                    {
                        sum += copy[j];
                        n++;
                    }
                }

                widths[k] = sum / n;
            }
        }

        var left = new List<SKPoint>(count + 1);
        var rightSide = new List<SKPoint>(count + 1);
        for (var k = 0; k <= count; k++)
        {
            var half = Math.Max(0.2f, widths[k] / 2);
            left.Add(new SKPoint(centres[k].X + normals[k].X * half, centres[k].Y + normals[k].Y * half));
            rightSide.Add(new SKPoint(centres[k].X - normals[k].X * half, centres[k].Y - normals[k].Y * half));
        }

        // fewer points along the long sides; the ends are round
        left = Simplify(left, 0.12f);
        rightSide = Simplify(rightSide, 0.12f);
        var first = 0;
        var last = count;
        var endRadius = Math.Max(0.2f, widths[last] / 2);
        var startRadius = Math.Max(0.2f, widths[first] / 2);

        outline.MoveTo(left[0]);
        foreach (var p in left.Skip(1))
        {
            outline.LineTo(p);
        }

        for (var step = 1; step < 10; step++)
        {
            var angle = Math.PI * step / 10;
            var c = (float)Math.Cos(angle);
            var sn = (float)Math.Sin(angle);
            outline.LineTo(
                centres[last].X + (normals[last].X * c + tangents[last].X * sn) * endRadius,
                centres[last].Y + (normals[last].Y * c + tangents[last].Y * sn) * endRadius);
        }

        for (var k = rightSide.Count - 1; k >= 0; k--)
        {
            outline.LineTo(rightSide[k]);
        }

        for (var step = 1; step < 10; step++)
        {
            var angle = Math.PI * step / 10;
            var c = (float)Math.Cos(angle);
            var sn = (float)Math.Sin(angle);
            outline.LineTo(
                centres[first].X + (-normals[first].X * c - tangents[first].X * sn) * startRadius,
                centres[first].Y + (-normals[first].Y * c - tangents[first].Y * sn) * startRadius);
        }

        outline.Close();
        return OutlineShape(outline, centre, color);
    }

    /// <summary>The shape of a stroke outline (and the line down its middle) in the 24 x 24 space every path shape is scaled from.</summary>
    private static Shape OutlineShape(SKPath outline, SKPath? centre, ArgbColor color)
    {
        var bounds = outline.TightBounds;
        var frameWidth = Math.Max(1f, bounds.Width);
        var frameHeight = Math.Max(1f, bounds.Height);
        var matrix = SKMatrix.CreateScaleTranslation(24f / frameWidth, 24f / frameHeight, -bounds.Left * 24f / frameWidth, -bounds.Top * 24f / frameHeight);
        using var local = new SKPath(outline);
        local.Transform(matrix);

        string? centreData = null;
        if (centre is not null)
        {
            using var localCentre = new SKPath(centre);
            localCentre.Transform(matrix);
            centreData = localCentre.ToSvgPathData();
        }

        return new Shape
        {
            Kind = ShapeKind.CombinedPath,
            Name = "Handwriting",
            X = bounds.Left,
            Y = bounds.Top,
            Width = frameWidth,
            Height = frameHeight,
            CustomPathData = local.ToSvgPathData(),
            CustomPathStrokeOnly = false,
            FillEnabled = true,
            FillColor = color,
            BorderColor = color,
            BorderThickness = 0,
            Freehand = true,
            InkCenterline = centreData,
        };
    }

    /// <summary>True when the point lies on the drawn stroke (within half its width and <paramref name="tolerance"/>): a stroke is picked where it is,
    /// not anywhere in its frame, so notes written over a plan do not catch the clicks meant for the plan.</summary>
    public static bool HitTest(Shape shape, double x, double y, double tolerance)
    {
        if (!shape.CustomPathStrokeOnly && shape.Kind == ShapeKind.CombinedPath)
        {
            // a stroke written with pressure is a filled shape: touching it anywhere inside is a hit
            using var area = ShapeGeometryFactory.Build(shape);
            if (area.Contains((float)x, (float)y))
            {
                return true;
            }
        }

        if (OutlineGeometry.NearestOnOutline(shape, new SKPoint((float)x, (float)y)) is { } near)
        {
            return near.Distance <= shape.BorderThickness / 2 + tolerance;
        }

        // a dot (the pen only touched the page) has no outline to measure to: it is a round dot where its frame is
        var reach = Math.Max(shape.Width, shape.Height) / 2 + shape.BorderThickness / 2 + tolerance;
        var dx = x - (shape.X + shape.Width / 2);
        var dy = y - (shape.Y + shape.Height / 2);
        return dx * dx + dy * dy <= reach * reach;
    }
}
