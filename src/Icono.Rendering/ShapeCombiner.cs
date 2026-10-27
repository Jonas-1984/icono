using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// Boolean path operations (Union/Trim) on two or more shapes' rendered outlines, using
/// SkiaSharp's native <see cref="SKPath.Op(SKPath, SKPathOp)"/> — no hand-rolled clipping needed.
/// The result is a new <see cref="ShapeKind.CombinedPath"/> shape whose geometry is normalized
/// into the same local 24x24 space every fixed-geometry kind uses (see
/// <see cref="ShapeGeometryFactory.Build"/>), so it moves/rotates/resizes exactly like any other
/// shape from then on.
/// </summary>
public static class ShapeCombiner
{
    /// <summary>Merges every shape's outline into one filled silhouette (the overlap included).</summary>
    public static Shape? Union(IReadOnlyList<Shape> shapes)
    {
        if (shapes.Count < 2)
        {
            return null;
        }

        var current = ShapeGeometryFactory.Build(shapes[0]);

        for (var i = 1; i < shapes.Count; i++)
        {
            using var next = ShapeGeometryFactory.Build(shapes[i]);
            var combined = current.Op(next, SKPathOp.Union);
            current.Dispose();

            if (combined is null)
            {
                return null;
            }

            current = combined;
        }

        return BuildCombinedShape(current, shapes[0]);
    }

    /// <summary>Combines every shape's geometry into ONE shape object — each input's own sub-path
    /// is kept exactly as drawn (no boolean fill-merge), just concatenated and re-normalized into
    /// the shared 24x24 space. Unlike <see cref="Union"/> (which needs real fillable areas to
    /// union and mangles open line geometry), this works for ANY mix of lines, stroke-only icons,
    /// and filled shapes — useful for turning several separate pieces (e.g. a multi-path SVG
    /// import's several CombinedPath shapes) into one object that moves/resizes/selects as a
    /// single unit without changing how any piece looks.</summary>
    public static Shape? Merge(IReadOnlyList<Shape> shapes)
    {
        if (shapes.Count < 2)
        {
            return null;
        }

        using var combined = new SKPath();
        foreach (var shape in shapes)
        {
            using var piece = ShapeGeometryFactory.Build(shape);
            combined.AddPath(piece);
        }

        var bounds = combined.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        using var normalized = new SKPath(combined);
        normalized.Offset(-bounds.Left, -bounds.Top);
        normalized.Transform(SKMatrix.CreateScale(24f / bounds.Width, 24f / bounds.Height));

        var template = shapes[0];

        // If ANY piece is a line/stroke-only icon, the merged result must stay in "fill closed
        // contours only" mode too — otherwise the generic fill pass would treat the whole
        // concatenated path as one nonzero-winding region and auto-fill that piece's open contour
        // into an unwanted blob (the exact bug already fixed for the fixed-geometry icon kinds).
        var strokeOnly = shapes.Any(IsEffectivelyStrokeOnly);

        return new Shape
        {
            Kind = ShapeKind.CombinedPath,
            X = bounds.Left,
            Y = bounds.Top,
            Width = Math.Max(1, bounds.Width),
            Height = Math.Max(1, bounds.Height),
            FillColor = template.FillColor,
            BorderColor = template.BorderColor,
            BorderThickness = template.BorderThickness,
            CustomPathData = normalized.ToSvgPathData(),
            CustomPathStrokeOnly = strokeOnly,
        };
    }

    /// <summary>The opposite of <see cref="Merge"/> ("explode"): a combined path falls apart into its single outlines, a polyline into its
    /// straight segments. Straight open pieces become lines / polylines, every other piece (a closed outline, a curve) a combined path of its
    /// own; each keeps the look of the original and lies exactly where it was. Null when the shape cannot be exploded or has just one piece.</summary>
    public static IReadOnlyList<Shape>? Explode(Shape source)
    {
        if (source.Kind == ShapeKind.Polyline && source.Points is { Count: > 2 } points)
        {
            var segments = new List<Shape>();
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var piece = NewLine(source, [new SKPoint((float)(source.X + points[i].X), (float)(source.Y + points[i].Y)),
                    new SKPoint((float)(source.X + points[i + 1].X), (float)(source.Y + points[i + 1].Y))]);
                segments.Add(piece);
            }

            return segments;
        }

        if (source.Kind != ShapeKind.CombinedPath)
        {
            return null;
        }

        using var built = ShapeGeometryFactory.Build(source);
        var pieces = new List<Shape>();
        foreach (var contour in SplitContours(built))
        {
            if (contour.Points is { } line)
            {
                contour.Path.Dispose();
                pieces.Add(NewLine(source, line));
                continue;
            }

            var bounds = contour.Path.Bounds;
            if (bounds.Width <= 0.01f && bounds.Height <= 0.01f)
            {
                contour.Path.Dispose();
                continue;
            }

            pieces.Add(NewCombinedPiece(source, contour.Path, bounds));
        }

        return pieces.Count > 1 ? pieces : null;
    }

    private static Shape NewLine(Shape source, IReadOnlyList<SKPoint> points)
    {
        var left = points.Min(p => p.X);
        var top = points.Min(p => p.Y);
        var line = new Shape
        {
            Kind = points.Count == 2 ? ShapeKind.Line : ShapeKind.Polyline,
            X = left,
            Y = top,
            Opacity = source.Opacity,
            BorderColor = source.BorderColor,
            BorderThickness = source.BorderThickness,
            FillEnabled = false,
            Dashed = source.Dashed,
            Points = points.Select(p => new CorePoint(p.X - left, p.Y - top)).ToList(),
        };
        PathBoundsCalculator.Recompute(line);
        return line;
    }

    private static Shape NewCombinedPiece(Shape source, SKPath path, SKRect bounds)
    {
        // a contour that is flat in one direction would divide by zero when it is fitted into the 24 x 24 box
        var width = Math.Max(bounds.Width, 0.01f);
        var height = Math.Max(bounds.Height, 0.01f);
        path.Offset(-bounds.Left, -bounds.Top);
        path.Transform(SKMatrix.CreateScale(24f / width, 24f / height));
        var data = path.ToSvgPathData();
        path.Dispose();

        return new Shape
        {
            Kind = ShapeKind.CombinedPath,
            X = bounds.Left,
            Y = bounds.Top,
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Opacity = source.Opacity,
            FillColor = source.FillColor,
            FillEnabled = source.FillEnabled,
            BorderColor = source.BorderColor,
            BorderThickness = source.BorderThickness,
            CustomPathData = data,
            CustomPathStrokeOnly = source.CustomPathStrokeOnly,
        };
    }

    /// <summary>One outline of a path: the path itself, plus its points when it is a plain open run of straight segments.</summary>
    private readonly record struct Contour(SKPath Path, List<SKPoint>? Points);

#pragma warning disable CS0618 // SKPath.MoveTo / LineTo ...: the builder API is not used elsewhere in the project yet
    private static List<Contour> SplitContours(SKPath path)
    {
        var result = new List<Contour>();
        using var iterator = path.CreateRawIterator();
        var pts = new SKPoint[4];
        SKPath? current = null;
        List<SKPoint>? straight = null;
        var closed = false;

        void Finish()
        {
            if (current is not null)
            {
                result.Add(new Contour(current, !closed && straight is { Count: >= 2 } ? straight : null));
            }

            current = null;
            straight = null;
            closed = false;
        }

        SKPathVerb verb;
        while ((verb = iterator.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    Finish();
                    current = new SKPath();
                    current.MoveTo(pts[0]);
                    straight = [pts[0]];
                    break;
                case SKPathVerb.Line:
                    current?.LineTo(pts[1]);
                    straight?.Add(pts[1]);
                    break;
                case SKPathVerb.Quad:
                    current?.QuadTo(pts[1], pts[2]);
                    straight = null;
                    break;
                case SKPathVerb.Conic:
                    current?.ConicTo(pts[1], pts[2], iterator.ConicWeight());
                    straight = null;
                    break;
                case SKPathVerb.Cubic:
                    current?.CubicTo(pts[1], pts[2], pts[3]);
                    straight = null;
                    break;
                case SKPathVerb.Close:
                    current?.Close();
                    closed = true;
                    break;
            }
        }

        Finish();
        return result;
    }
#pragma warning restore CS0618

    private static bool IsEffectivelyStrokeOnly(Shape shape) =>
        shape.Kind == ShapeKind.CombinedPath ? shape.CustomPathStrokeOnly : ShapeGeometryFactory.IsStrokeOnly(shape.Kind);

    /// <summary>Cuts away the part of <paramref name="back"/> that <paramref name="front"/>
    /// covers — the front shape is used as a cutter and isn't kept itself.</summary>
    public static Shape? Trim(Shape front, Shape back)
    {
        using var frontPath = ShapeGeometryFactory.Build(front);
        using var backPath = ShapeGeometryFactory.Build(back);

        var result = backPath.Op(frontPath, SKPathOp.Difference);
        return result is null ? null : BuildCombinedShape(result, back);
    }

    private static Shape? BuildCombinedShape(SKPath resultPath, Shape template)
    {
        var bounds = resultPath.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            resultPath.Dispose();
            return null;
        }

        using var normalized = new SKPath(resultPath);
        resultPath.Dispose();
        normalized.Offset(-bounds.Left, -bounds.Top);
        normalized.Transform(SKMatrix.CreateScale(24f / bounds.Width, 24f / bounds.Height));

        return new Shape
        {
            Kind = ShapeKind.CombinedPath,
            X = bounds.Left,
            Y = bounds.Top,
            Width = Math.Max(1, bounds.Width),
            Height = Math.Max(1, bounds.Height),
            FillColor = template.FillColor,
            BorderColor = template.BorderColor,
            BorderThickness = template.BorderThickness,
            CustomPathData = normalized.ToSvgPathData(),
        };
    }
}
