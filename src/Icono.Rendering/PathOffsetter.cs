using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Builds a parallel-curve "offset" copy of a shape's outline — grows/shrinks a closed
/// shape's border by a fixed distance, or thickens an open line into two parallel offset curves
/// (see the "Offset" tool). Implemented via <see cref="SKPaint.GetFillPath"/> (converts a STROKE
/// of the requested width into its equivalent fillable band) combined with the same boolean
/// Union/Difference ops <see cref="ShapeCombiner"/> already uses — no hand-rolled polygon-offset
/// math needed.</summary>
public static class PathOffsetter
{
    public static Shape? Offset(Shape shape, double distance)
    {
        if (distance == 0)
        {
            return null;
        }

        using var source = ShapeGeometryFactory.Build(shape);

        // "Open" = no closed contour anywhere in the geometry (a plain Line/Polyline/Curve, or a
        // CombinedPath piece cut from one) — there's no "inside" to grow/shrink, so the offset
        // result is shown as an outline only. A shape with at least one closed contour (even a
        // mixed one like Umbrella's filled canopy + open handle) gets the normal grow/shrink
        // treatment instead.
        using var closedOnly = ShapeGeometryFactory.ExtractClosedContoursOnly(source);
        var isOpen = closedOnly.Bounds is { Width: <= 0 } or { Height: <= 0 };

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Abs(distance) * 2f,
            // Miter (sharp) corners for a closed shape — its silhouette must keep reading as the
            // same shape, just grown/shrunk, not gain rounded corners it never had. An open line
            // has no "shape" to preserve, so Round stays there (matches the reference "offset
            // polyline" look — a capsule-ended band tracing the curve).
            StrokeJoin = isOpen ? SKStrokeJoin.Round : SKStrokeJoin.Miter,
            StrokeCap = SKStrokeCap.Round,
        };

        var band = new SKPath();
        if (!strokePaint.GetFillPath(source, band))
        {
            band.Dispose();
            return null;
        }

        SKPath result;
        bool fillEnabled;

        if (isOpen)
        {
            // No "inside"/"outside" for an open line — the stroked band itself, shown as an
            // outline only (no fill), IS the "two parallel offset curves" result.
            result = band;
            fillEnabled = false;
        }
        else
        {
            result = distance > 0
                ? source.Op(band, SKPathOp.Union) ?? band
                : source.Op(band, SKPathOp.Difference) ?? band;
            if (!ReferenceEquals(result, band))
            {
                band.Dispose();
            }

            fillEnabled = true;
        }

        using var finalResult = result;
        var bounds = finalResult.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        using var normalized = new SKPath(finalResult);
        normalized.Offset(-bounds.Left, -bounds.Top);
        normalized.Transform(SKMatrix.CreateScale(24f / bounds.Width, 24f / bounds.Height));

        return new Shape
        {
            Kind = ShapeKind.CombinedPath,
            X = bounds.Left,
            Y = bounds.Top,
            Width = Math.Max(1, bounds.Width),
            Height = Math.Max(1, bounds.Height),
            FillColor = shape.FillColor,
            FillEnabled = fillEnabled,
            BorderColor = shape.BorderColor,
            BorderThickness = shape.BorderThickness > 0 ? shape.BorderThickness : 2,
            CustomPathData = normalized.ToSvgPathData(),
            CustomPathStrokeOnly = isOpen,
        };
    }
}
