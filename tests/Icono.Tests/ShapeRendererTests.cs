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
}
