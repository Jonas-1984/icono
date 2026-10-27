using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class TextOutlinerTests
{
    [Fact]
    public void ConvertToShape_WithTypedText_ProducesACombinedPathShape()
    {
        var text = new Shape
        {
            Kind = ShapeKind.Text,
            X = 0,
            Y = 0,
            Width = 100,
            Height = 40,
            TextContent = "Hi",
            TextFontSize = 24,
            FillColor = ArgbColor.FromRgb(10, 20, 30),
        };

        var result = TextOutliner.ConvertToShape(text);

        Assert.NotNull(result);
        Assert.Equal(ShapeKind.CombinedPath, result!.Kind);
        Assert.NotNull(result.CustomPathData);
        Assert.False(result.CustomPathStrokeOnly);
        Assert.Equal(text.FillColor, result.FillColor);

        using var built = ShapeGeometryFactory.Build(result);
        Assert.True(built.Bounds.Width > 0);
        Assert.True(built.Bounds.Height > 0);
    }

    [Fact]
    public void ConvertToShape_WithoutTextContent_ReturnsNull()
    {
        var text = new Shape { Kind = ShapeKind.Text, X = 0, Y = 0, Width = 40, Height = 20 };

        Assert.Null(TextOutliner.ConvertToShape(text));
    }

    [Fact]
    public void ConvertToShape_NonTextKind_ReturnsNull()
    {
        var rect = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 40, Height = 20 };

        Assert.Null(TextOutliner.ConvertToShape(rect));
    }

    [Fact]
    public void ConvertToShape_CarriesOverRotationAsAShapeProperty_NotBakedIntoGeometry()
    {
        // Rotation must ride along as Shape.Rotation (like every other kind) rather than being
        // pre-rotated into the path data, so the outlined result stays correctly resizable.
        var text = new Shape
        {
            Kind = ShapeKind.Text, X = 0, Y = 0, Width = 100, Height = 40,
            TextContent = "A", TextFontSize = 24, Rotation = 45,
        };

        var result = TextOutliner.ConvertToShape(text);

        Assert.NotNull(result);
        Assert.Equal(45, result!.Rotation);
    }

    [Theory]
    [InlineData(TextHorizontalAlign.Left, TextVerticalAlign.Top)]
    [InlineData(TextHorizontalAlign.Right, TextVerticalAlign.Bottom)]
    [InlineData(TextHorizontalAlign.Center, TextVerticalAlign.Middle)]
    public void TextInALargerFrame_SitsAtTheChosenPosition(TextHorizontalAlign h, TextVerticalAlign v)
    {
        var text = new Shape
        {
            Kind = ShapeKind.Text, X = 50, Y = 40, Width = 300, Height = 150,
            TextContent = "Hi", TextFontSize = 24, TextHAlign = h, TextVAlign = v,
        };

        using var path = TextOutliner.BuildTextPath(text);

        Assert.NotNull(path);
        var b = path!.Bounds;
        if (h == TextHorizontalAlign.Left) { Assert.InRange(b.Left, 53, 55); }
        else if (h == TextHorizontalAlign.Right) { Assert.InRange(b.Right, 345, 347); }
        else { Assert.InRange(b.MidX, 199, 201); }

        if (v == TextVerticalAlign.Top) { Assert.InRange(b.Top, 43, 45); }
        else if (v == TextVerticalAlign.Bottom) { Assert.InRange(b.Bottom, 185, 187); }
        else { Assert.InRange(b.MidY, 114, 116); }
    }
}
