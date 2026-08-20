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
