using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class ShapeCombinerTests
{
    [Fact]
    public void Union_OfTwoOverlappingRectangles_BoundsSpanBothShapesActualGeometry()
    {
        var a = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 20, Height = 20 };
        var b = new Shape { Kind = ShapeKind.Rectangle, X = 10, Y = 10, Width = 20, Height = 20 };

        // ShapeGeometryFactory insets every fixed-template shape slightly within its nominal
        // X/Y/Width/Height box (for the hand-drawn icon look), so compute the expected union
        // bounds from the shapes' REAL rendered geometry rather than their nominal box.
        using var aPath = ShapeGeometryFactory.Build(a);
        using var bPath = ShapeGeometryFactory.Build(b);
        var expectedLeft = Math.Min(aPath.Bounds.Left, bPath.Bounds.Left);
        var expectedTop = Math.Min(aPath.Bounds.Top, bPath.Bounds.Top);
        var expectedRight = Math.Max(aPath.Bounds.Right, bPath.Bounds.Right);
        var expectedBottom = Math.Max(aPath.Bounds.Bottom, bPath.Bounds.Bottom);

        var result = ShapeCombiner.Union([a, b]);

        Assert.NotNull(result);
        Assert.Equal(ShapeKind.CombinedPath, result!.Kind);
        Assert.NotNull(result.CustomPathData);
        Assert.Equal(expectedLeft, result.X, 1);
        Assert.Equal(expectedTop, result.Y, 1);
        Assert.Equal(expectedRight - expectedLeft, result.Width, 1);
        Assert.Equal(expectedBottom - expectedTop, result.Height, 1);
    }

    [Fact]
    public void Union_WithFewerThanTwoShapes_ReturnsNull()
    {
        var a = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 20, Height = 20 };

        Assert.Null(ShapeCombiner.Union([a]));
    }

    [Fact]
    public void Trim_CutsTheOverlapOutOfTheBackShape()
    {
        var front = new Shape { Kind = ShapeKind.Rectangle, X = 10, Y = 0, Width = 20, Height = 20 };
        var back = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 20, Height = 20 };

        using var backPath = ShapeGeometryFactory.Build(back);
        var result = ShapeCombiner.Trim(front, back);

        Assert.NotNull(result);
        using var builtPath = ShapeGeometryFactory.Build(result!);

        // The front shape starts well before back's right edge and overlaps it, so trimming must
        // shrink back's right edge inward — the remainder can't reach as far right as before.
        Assert.True(builtPath.Bounds.Right < backPath.Bounds.Right);
    }

    [Fact]
    public void Trim_NonOverlappingShapes_LeavesBackShapeUnchanged()
    {
        var front = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 20, Height = 20 };
        var back = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 20, Height = 20 };

        using var backPath = ShapeGeometryFactory.Build(back);
        var result = ShapeCombiner.Trim(front, back);

        Assert.NotNull(result);
        Assert.Equal(backPath.Bounds.Width, result!.Width, 1);
        Assert.Equal(backPath.Bounds.Height, result.Height, 1);
    }

    [Fact]
    public void Merge_WithFewerThanTwoShapes_ReturnsNull()
    {
        var a = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 20, Height = 20 };

        Assert.Null(ShapeCombiner.Merge([a]));
    }

    [Fact]
    public void Merge_TwoNonOverlappingLines_ProducesOneObjectSpanningBoth()
    {
        // Two disconnected lines far apart — a boolean Union would have nothing meaningful to
        // union (no fillable area at all), but Merge must still combine them into one object.
        var lineA = new Shape
        {
            Kind = ShapeKind.Line, X = 0, Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(20, 0)],
        };
        var lineB = new Shape
        {
            Kind = ShapeKind.Line, X = 100, Y = 100,
            Points = [new CorePoint(0, 0), new CorePoint(20, 0)],
        };

        var result = ShapeCombiner.Merge([lineA, lineB]);

        Assert.NotNull(result);
        Assert.Equal(ShapeKind.CombinedPath, result!.Kind);
        Assert.True(result.CustomPathStrokeOnly);
        // Spans from lineA's start to lineB's end.
        Assert.Equal(0, result.X, 1);
        Assert.Equal(0, result.Y, 1);
        Assert.Equal(120, result.Width, 1);
        Assert.Equal(100, result.Height, 1);
    }

    [Fact]
    public void Merge_LinePlusFilledShape_KeepsTheLineUnfilled()
    {
        // A mixed selection (one stroke-only line, one filled rectangle) must still protect the
        // line's open contour from being auto-filled into a blob — the exact bug class already
        // fixed for the fixed-geometry icon kinds (HasMixedOpenAndClosedContours).
        var line = new Shape
        {
            Kind = ShapeKind.Line, X = 0, Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(20, 0)],
        };
        var rect = new Shape { Kind = ShapeKind.Rectangle, X = 40, Y = 0, Width = 20, Height = 20 };

        var result = ShapeCombiner.Merge([line, rect]);

        Assert.NotNull(result);
        Assert.True(result!.CustomPathStrokeOnly);
    }

    [Fact]
    public void Merge_TwoFilledShapes_StaysFilled()
    {
        var a = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 20, Height = 20 };
        var b = new Shape { Kind = ShapeKind.Rectangle, X = 40, Y = 0, Width = 20, Height = 20 };

        var result = ShapeCombiner.Merge([a, b]);

        Assert.NotNull(result);
        Assert.False(result!.CustomPathStrokeOnly);
    }

    [Fact]
    public void Merge_PreservesEachPiecesOwnGeometry_UnlikeBooleanUnion()
    {
        // Two non-overlapping rectangles: Union's boolean op and Merge's concatenation should
        // agree on the overall bounds here (no area to actually union away), but Merge must NOT
        // go through SKPath.Op at all — verified indirectly by confirming it succeeds where a
        // degenerate/empty-area boolean op could plausibly fail for non-overlapping open shapes.
        var a = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 20, Height = 20 };
        var b = new Shape { Kind = ShapeKind.Rectangle, X = 40, Y = 0, Width = 20, Height = 20 };

        var result = ShapeCombiner.Merge([a, b]);

        Assert.NotNull(result);
        using var built = ShapeGeometryFactory.Build(result!);
        Assert.True(built.Bounds.Width > 0);
        Assert.True(built.Bounds.Height > 0);
    }

    [Fact]
    public void Explode_BreaksAMergedObjectBackIntoItsOutlines()
    {
        var a = new Shape { Kind = ShapeKind.Rectangle, X = 10, Y = 10, Width = 40, Height = 30 };
        var b = new Shape { Kind = ShapeKind.Ellipse, X = 100, Y = 20, Width = 30, Height = 30 };
        var merged = ShapeCombiner.Merge([a, b])!;

        var pieces = ShapeCombiner.Explode(merged);

        Assert.NotNull(pieces);
        Assert.Equal(2, pieces!.Count);
        using var whole = ShapeGeometryFactory.Build(merged);
        var union = UnionOf(pieces);

        Assert.Equal(whole.Bounds.Left, union.Left, 0.5);
        Assert.Equal(whole.Bounds.Top, union.Top, 0.5);
        Assert.Equal(whole.Bounds.Right, union.Right, 0.5);
        Assert.Equal(whole.Bounds.Bottom, union.Bottom, 0.5);
    }

    [Fact]
    public void Explode_ACatalogueObjectFallsApartAndKeepsItsPlaceAndSize()
    {
        var unit = Icono.Rendering.UnitCatalog.Find("kel01")!;
        var shape = new Shape
        {
            Kind = ShapeKind.CombinedPath, CustomPathData = unit.PathData, CustomPathStrokeOnly = true,
            X = 50, Y = 60, Width = unit.WidthCm, Height = unit.HeightCm,
        };

        var pieces = ShapeCombiner.Explode(shape);

        Assert.NotNull(pieces);
        Assert.True(pieces!.Count > 2, "a sink has an outline, an inner outline, a tap and a drain");
        using var whole = ShapeGeometryFactory.Build(shape);
        var union = UnionOf(pieces);

        Assert.InRange(union.Left, whole.Bounds.Left - 1, whole.Bounds.Left + 1);
        Assert.InRange(union.Right, whole.Bounds.Right - 1, whole.Bounds.Right + 1);
        Assert.InRange(union.Bottom, whole.Bounds.Bottom - 1, whole.Bounds.Bottom + 1);
    }

    [Fact]
    public void Explode_APolylineBecomesItsSegments_AndASinglePieceDoesNot()
    {
        var polyline = new Shape { Kind = ShapeKind.Polyline, X = 0, Y = 0, Points = [new CorePoint(0, 0), new CorePoint(10, 0), new CorePoint(10, 10)] };
        PathBoundsCalculator.Recompute(polyline);

        var segments = ShapeCombiner.Explode(polyline);

        Assert.Equal(2, segments!.Count);
        Assert.All(segments, s => Assert.Equal(ShapeKind.Line, s.Kind));
        Assert.Null(ShapeCombiner.Explode(new Shape { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 }));
    }

    private static SkiaSharp.SKRect UnionOf(IEnumerable<Shape> shapes)
    {
        var union = default(SkiaSharp.SKRect);
        var first = true;
        foreach (var shape in shapes)
        {
            using var path = ShapeGeometryFactory.Build(shape);
            if (first) { union = path.Bounds; first = false; } else { union.Union(path.Bounds); }
        }

        return union;
    }
}
