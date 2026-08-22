using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class PathOffsetterTests
{
    [Fact]
    public void Offset_ZeroDistance_ReturnsNull()
    {
        var rect = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 40, Height = 40 };

        Assert.Null(PathOffsetter.Offset(rect, 0));
    }

    [Fact]
    public void Offset_PositiveDistance_GrowsAClosedShapeOutward()
    {
        var rect = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 40, Height = 40 };
        using var sourcePath = ShapeGeometryFactory.Build(rect);

        var result = PathOffsetter.Offset(rect, 6);

        Assert.NotNull(result);
        Assert.True(result!.FillEnabled);
        Assert.False(result.CustomPathStrokeOnly);

        using var resultPath = ShapeGeometryFactory.Build(result);
        Assert.True(resultPath.Bounds.Width > sourcePath.Bounds.Width);
        Assert.True(resultPath.Bounds.Height > sourcePath.Bounds.Height);
    }

    [Fact]
    public void Offset_NegativeDistance_ShrinksAClosedShapeInward()
    {
        var rect = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 40, Height = 40 };
        using var sourcePath = ShapeGeometryFactory.Build(rect);

        var result = PathOffsetter.Offset(rect, -6);

        Assert.NotNull(result);
        using var resultPath = ShapeGeometryFactory.Build(result!);
        Assert.True(resultPath.Bounds.Width < sourcePath.Bounds.Width);
        Assert.True(resultPath.Bounds.Height < sourcePath.Bounds.Height);
    }

    [Fact]
    public void Offset_OpenLine_ProducesAnOutlineOnlyBand()
    {
        var line = new Shape
        {
            Kind = ShapeKind.Line, X = 0, Y = 0,
            Points = [new CorePoint(0, 0), new CorePoint(40, 0)],
        };

        var result = PathOffsetter.Offset(line, 4);

        Assert.NotNull(result);
        Assert.False(result!.FillEnabled);
        Assert.True(result.CustomPathStrokeOnly);

        using var resultPath = ShapeGeometryFactory.Build(result);
        // The band straddles the line on both sides, so its height must clearly exceed the
        // original zero-height line.
        Assert.True(resultPath.Bounds.Height > 2);
    }

    [Fact]
    public void Offset_ClosedShape_KeepsCornersSharpInsteadOfRoundingThem()
    {
        // An exact axis-aligned square, in the local 24x24 space so no template-inset/rescale
        // fuzziness affects the corner position.
        // Placed at (20,20), not (0,0) — the grown result's bounds shift up/left by the offset
        // distance, and this keeps them safely on-canvas (positive) for pixel sampling below.
        var square = new Shape
        {
            Kind = ShapeKind.CombinedPath, X = 20, Y = 20, Width = 24, Height = 24,
            CustomPathData = "M0 0 L24 0 L24 24 L0 24 Z",
        };

        var result = PathOffsetter.Offset(square, 6);
        Assert.NotNull(result);

        using var bitmap = new SkiaSharp.SKBitmap(80, 80);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        ShapeRenderer.Draw(canvas, result!);

        // 2px inset from each of the grown shape's 4 bounding-box corners: a MITER (sharp) join
        // reaches all the way into the corner, so this pixel is opaque; a ROUND join cuts the
        // corner off, leaving it transparent — exactly the bug this guards against.
        var x0 = (int)result!.X + 2;
        var y0 = (int)result.Y + 2;
        var x1 = (int)(result.X + result.Width) - 3;
        var y1 = (int)(result.Y + result.Height) - 3;

        Assert.True(bitmap.GetPixel(x0, y0).Alpha > 0, "top-left corner should stay sharp");
        Assert.True(bitmap.GetPixel(x1, y0).Alpha > 0, "top-right corner should stay sharp");
        Assert.True(bitmap.GetPixel(x0, y1).Alpha > 0, "bottom-left corner should stay sharp");
        Assert.True(bitmap.GetPixel(x1, y1).Alpha > 0, "bottom-right corner should stay sharp");
    }

    [Fact]
    public void Offset_MixedOpenAndClosedKind_TreatedAsClosed()
    {
        // Umbrella has a filled closed canopy plus a decorative open handle line — the shape as a
        // whole has "an inside", so offsetting it must use the grow/shrink path, not the
        // outline-only open-line path.
        var umbrella = new Shape { Kind = ShapeKind.Umbrella, X = 0, Y = 0, Width = 60, Height = 60 };

        var result = PathOffsetter.Offset(umbrella, 4);

        Assert.NotNull(result);
        Assert.True(result!.FillEnabled);
        Assert.False(result.CustomPathStrokeOnly);
    }
}
