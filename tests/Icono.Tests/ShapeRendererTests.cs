using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class ShapeRendererTests
{
    private static SKCanvas CreateCanvas(SKBitmap bitmap) => new(bitmap);

    [Fact]
    public void Draw_TextShapeWithContent_DoesNotThrowAndSkipsTheGenericFillPath()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Text,
            X = 0,
            Y = 0,
            Width = 100,
            Height = 40,
            TextContent = "Hello",
            TextFontSize = 20,
        };

        using var bitmap = new SKBitmap(100, 40);
        using var canvas = CreateCanvas(bitmap);

        var exception = Record.Exception(() => ShapeRenderer.Draw(canvas, shape));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_TextShapeWithoutContent_FallsBackToThePlaceholderGlyphWithoutThrowing()
    {
        var shape = new Shape { Kind = ShapeKind.Text, X = 0, Y = 0, Width = 24, Height = 24 };

        using var bitmap = new SKBitmap(24, 24);
        using var canvas = CreateCanvas(bitmap);

        var exception = Record.Exception(() => ShapeRenderer.Draw(canvas, shape));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_BoldItalicTextShape_DoesNotThrow()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Text,
            X = 0,
            Y = 0,
            Width = 120,
            Height = 40,
            TextContent = "Bold Italic",
            TextFontSize = 18,
            TextBold = true,
            TextItalic = true,
        };

        using var bitmap = new SKBitmap(120, 40);
        using var canvas = CreateCanvas(bitmap);

        var exception = Record.Exception(() => ShapeRenderer.Draw(canvas, shape));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_UnderlinedTextShape_DoesNotThrow()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Text,
            X = 0,
            Y = 0,
            Width = 120,
            Height = 40,
            TextContent = "Underlined",
            TextFontSize = 18,
            TextUnderline = true,
        };

        using var bitmap = new SKBitmap(120, 40);
        using var canvas = CreateCanvas(bitmap);

        var exception = Record.Exception(() => ShapeRenderer.Draw(canvas, shape));

        Assert.Null(exception);
    }

    private static byte[] EncodeTinyPng()
    {
        using var source = new SKBitmap(4, 4);
        using var sourceCanvas = new SKCanvas(source);
        sourceCanvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Draw_RasterImageShapeWithBytes_DoesNotThrowAndSkipsTheGenericFillPath()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.RasterImage,
            X = 0,
            Y = 0,
            Width = 40,
            Height = 40,
            ImageBytes = EncodeTinyPng(),
        };

        using var bitmap = new SKBitmap(40, 40);
        using var canvas = CreateCanvas(bitmap);

        var exception = Record.Exception(() => ShapeRenderer.Draw(canvas, shape));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_RasterImageShapeWithoutBytes_FallsBackWithoutThrowing()
    {
        var shape = new Shape { Kind = ShapeKind.RasterImage, X = 0, Y = 0, Width = 24, Height = 24 };

        using var bitmap = new SKBitmap(24, 24);
        using var canvas = CreateCanvas(bitmap);

        var exception = Record.Exception(() => ShapeRenderer.Draw(canvas, shape));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Draw_CombinedPathShape_RespectsPerShapeCustomPathStrokeOnly(bool strokeOnly)
    {
        var shape = new Shape
        {
            Kind = ShapeKind.CombinedPath,
            X = 0,
            Y = 0,
            Width = 40,
            Height = 40,
            CustomPathData = "M0 0 L20 0 L20 20 L0 20 Z",
            CustomPathStrokeOnly = strokeOnly,
            BorderThickness = 2,
        };

        using var bitmap = new SKBitmap(40, 40);
        using var canvas = CreateCanvas(bitmap);

        var exception = Record.Exception(() => ShapeRenderer.Draw(canvas, shape));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_RasterImageShapeWithBorderShadowBlurCornerRadiusAndFlip_DoesNotThrow()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.RasterImage,
            X = 0,
            Y = 0,
            Width = 40,
            Height = 40,
            Rotation = 30,
            FlipHorizontal = true,
            FlipVertical = true,
            CornerRadius = 8,
            BorderThickness = 3,
            BlurRadius = 4,
            ShadowEnabled = true,
            ImageBytes = EncodeTinyPng(),
        };

        using var bitmap = new SKBitmap(60, 60);
        using var canvas = CreateCanvas(bitmap);

        var exception = Record.Exception(() => ShapeRenderer.Draw(canvas, shape));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_ShapeWithFillDisabled_SkipsFillButStillDrawsBorder()
    {
        var filled = new SKBitmap(40, 40);
        using (var canvas = CreateCanvas(filled))
        {
            ShapeRenderer.Draw(canvas, new Shape
            {
                Kind = ShapeKind.Rectangle, X = 5, Y = 5, Width = 30, Height = 30,
                FillColor = ArgbColor.FromRgb(255, 0, 0), BorderThickness = 0,
            });
        }

        var unfilled = new SKBitmap(40, 40);
        using (var canvas = CreateCanvas(unfilled))
        {
            ShapeRenderer.Draw(canvas, new Shape
            {
                Kind = ShapeKind.Rectangle, X = 5, Y = 5, Width = 30, Height = 30,
                FillColor = ArgbColor.FromRgb(255, 0, 0), FillEnabled = false, BorderThickness = 0,
            });
        }

        // Center pixel: solid red when filled, still transparent when FillEnabled=false.
        Assert.Equal(255, filled.GetPixel(20, 20).Alpha);
        Assert.Equal(0, unfilled.GetPixel(20, 20).Alpha);
    }

    [Fact]
    public void Draw_Box3DWithFillDisabled_SkipsFacesButStillDrawsEdges()
    {
        var unfilled = new SKBitmap(80, 80);
        using var canvas = CreateCanvas(unfilled);

        ShapeRenderer.Draw(canvas, new Shape
        {
            Kind = ShapeKind.Box3D, X = 10, Y = 10, Width = 40, Height = 40, Depth3D = 8,
            FillColor = ArgbColor.FromRgb(255, 0, 0), FillEnabled = false, BorderThickness = 2,
        });

        // The front face's interior (well inside the outline, away from the border stroke) must
        // stay fully transparent — DrawBox3D used to fill all three faces unconditionally.
        Assert.Equal(0, unfilled.GetPixel(35, 35).Alpha);
    }

    [Fact]
    public void Draw_PartiallyTransparentFillColor_ReducesTheRenderedAlphaBelowFullOpacity()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Rectangle,
            X = 5,
            Y = 5,
            Width = 30,
            Height = 30,
            FillColor = new ArgbColor(128, 255, 0, 0),
            Opacity = 100,
            BorderThickness = 0,
        };

        using var bitmap = new SKBitmap(40, 40);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        var pixelAlpha = bitmap.GetPixel(20, 20).Alpha;

        // A half-transparent FillColor must actually render half-transparent, not get silently
        // upgraded to fully opaque by the shape-level Opacity (100%) overwriting it.
        Assert.InRange(pixelAlpha, 100, 156);
    }

    [Fact]
    public void Draw_StrokeOnlyKindWithClosedContour_NowActuallyFillsIt()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.TrafficCone,
            X = 0,
            Y = 0,
            Width = 240,
            Height = 240,
            FillColor = ArgbColor.FromRgb(255, 0, 0),
            BorderThickness = 0,
        };

        Assert.True(ShapeGeometryFactory.IsStrokeOnly(ShapeKind.TrafficCone));

        using var bitmap = new SKBitmap(240, 240);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        // Well inside the cone body's closed silhouette (template-local ~(12, 8) scaled 10x) —
        // before the fix, "stroke-only" kinds never drew a fill pass at all, so FillEnabled had no
        // effect and this stayed fully transparent regardless of the checkbox/color chosen.
        Assert.True(bitmap.GetPixel(120, 80).Alpha > 0);
    }

    [Fact]
    public void Draw_PureOpenLineKind_StillDrawsNoFill()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Arrow,
            X = 0,
            Y = 0,
            Width = 24,
            Height = 24,
            FillColor = ArgbColor.FromRgb(255, 0, 0),
            BorderThickness = 0,
        };

        using var bitmap = new SKBitmap(24, 24);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        // A shape with no closed contour at all must still render nothing for the fill pass —
        // the generalized "fill closed contours" logic must not invent an area where none exists.
        Assert.Equal(0, bitmap.GetPixel(12, 12).Alpha);
    }

    [Theory]
    [InlineData(ShapeKind.Cylinder)]
    [InlineData(ShapeKind.Cone)]
    public void Draw_CylinderAndCone_FillHasNoGapWhereTheEllipseMeetsTheBody(ShapeKind kind)
    {
        // The rim/base ellipse and the body outline are two separate closed contours in one path,
        // filled with the default nonzero winding rule. If they wind in OPPOSITE directions, their
        // overlap cancels out instead of unioning — leaving a transparent gap straight through the
        // ellipse where it meets the body (this exact bug, confirmed by screenshot).
        var shape = new Shape
        {
            Kind = kind, X = 0, Y = 0, Width = 240, Height = 240,
            FillColor = ArgbColor.FromRgb(50, 50, 50), BorderThickness = 0,
        };

        using var bitmap = new SKBitmap(240, 240);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        // Center column, spanning the ellipse's own vertical extent — every pixel in the rim/base
        // ellipse's interior must be filled, with no transparent band cutting through it.
        var ellipseCenterY = kind == ShapeKind.Cylinder ? 50 : 190;
        for (var dy = -25; dy <= 25; dy += 5)
        {
            Assert.True(bitmap.GetPixel(120, ellipseCenterY + dy).Alpha > 0);
        }
    }

    [Fact]
    public void Draw_FadeTopToBottom_SolidAtTopFadesTowardBottom()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.CombinedPath, X = 0, Y = 0, Width = 40, Height = 40,
            CustomPathData = "M0 0 L24 0 L24 24 L0 24 Z",
            FillColor = ArgbColor.FromRgb(255, 0, 0), BorderThickness = 0, FadeAmount = 100,
            FadeDirection = FadeDirection.TopToBottom,
        };

        using var bitmap = new SKBitmap(40, 40);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        Assert.True(bitmap.GetPixel(20, 2).Alpha > bitmap.GetPixel(20, 37).Alpha);
    }

    [Fact]
    public void Draw_FadeBottomToTop_IsTheReverseOfTopToBottom()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.CombinedPath, X = 0, Y = 0, Width = 40, Height = 40,
            CustomPathData = "M0 0 L24 0 L24 24 L0 24 Z",
            FillColor = ArgbColor.FromRgb(255, 0, 0), BorderThickness = 0, FadeAmount = 100,
            FadeDirection = FadeDirection.BottomToTop,
        };

        using var bitmap = new SKBitmap(40, 40);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        Assert.True(bitmap.GetPixel(20, 37).Alpha > bitmap.GetPixel(20, 2).Alpha);
    }

    [Fact]
    public void Draw_FadeLeftToRight_SolidAtLeftFadesTowardRight()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.CombinedPath, X = 0, Y = 0, Width = 40, Height = 40,
            CustomPathData = "M0 0 L24 0 L24 24 L0 24 Z",
            FillColor = ArgbColor.FromRgb(255, 0, 0), BorderThickness = 0, FadeAmount = 100,
            FadeDirection = FadeDirection.LeftToRight,
        };

        using var bitmap = new SKBitmap(40, 40);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        Assert.True(bitmap.GetPixel(2, 20).Alpha > bitmap.GetPixel(37, 20).Alpha);
    }

    [Fact]
    public void Draw_FadeVerticalBothSides_SolidInMiddleFadesTowardBothEdges()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.CombinedPath, X = 0, Y = 0, Width = 40, Height = 40,
            CustomPathData = "M0 0 L24 0 L24 24 L0 24 Z",
            FillColor = ArgbColor.FromRgb(255, 0, 0), BorderThickness = 0, FadeAmount = 100,
            FadeDirection = FadeDirection.VerticalBothSides,
        };

        using var bitmap = new SKBitmap(40, 40);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        var middle = bitmap.GetPixel(20, 20).Alpha;
        var top = bitmap.GetPixel(20, 1).Alpha;
        var bottom = bitmap.GetPixel(20, 38).Alpha;

        Assert.True(middle > top);
        Assert.True(middle > bottom);
    }

    [Fact]
    public void Draw_FadeHorizontalBothSides_SolidInMiddleFadesTowardBothEdges()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.CombinedPath, X = 0, Y = 0, Width = 40, Height = 40,
            CustomPathData = "M0 0 L24 0 L24 24 L0 24 Z",
            FillColor = ArgbColor.FromRgb(255, 0, 0), BorderThickness = 0, FadeAmount = 100,
            FadeDirection = FadeDirection.HorizontalBothSides,
        };

        using var bitmap = new SKBitmap(40, 40);
        using var canvas = CreateCanvas(bitmap);
        ShapeRenderer.Draw(canvas, shape);

        var middle = bitmap.GetPixel(20, 20).Alpha;
        var left = bitmap.GetPixel(1, 20).Alpha;
        var right = bitmap.GetPixel(38, 20).Alpha;

        Assert.True(middle > left);
        Assert.True(middle > right);
    }
}
