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
}
