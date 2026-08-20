using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class SvgImporterTests
{
    [Fact]
    public void ExtractPathData_FindsEveryPathElementsDValue()
    {
        const string svg = """
            <svg viewBox="0 0 24 24">
              <path d="M0 0 L10 0 L10 10 Z" />
              <path d='M2 2 L8 2 L8 8 Z' />
            </svg>
            """;

        var result = SvgImporter.ExtractPathData(svg);

        Assert.Equal(2, result.Count);
        Assert.Equal("M0 0 L10 0 L10 10 Z", result[0]);
        Assert.Equal("M2 2 L8 2 L8 8 Z", result[1]);
    }

    [Fact]
    public void ExtractPathData_WithNoPathTags_TreatsWholeInputAsBarePathData()
    {
        const string bareData = "M0 0 L10 0 L10 10 Z";

        var result = SvgImporter.ExtractPathData(bareData);

        Assert.Single(result);
        Assert.Equal(bareData, result[0]);
    }

    [Fact]
    public void ExtractPathData_WithBlankInput_ReturnsEmpty()
    {
        var result = SvgImporter.ExtractPathData("   ");

        Assert.Empty(result);
    }

    [Fact]
    public void BuildShapes_SinglePath_ProducesOneCombinedPathShapeAtTheRequestedPlacement()
    {
        var shapes = SvgImporter.BuildShapes(
            ["M0 0 L20 0 L20 20 L0 20 Z"],
            ArgbColor.FromRgb(1, 2, 3), ArgbColor.FromRgb(4, 5, 6), 2,
            placeX: 10, placeY: 20, placeWidth: 100, placeHeight: 80);

        var shape = Assert.Single(shapes);
        Assert.Equal(ShapeKind.CombinedPath, shape.Kind);
        Assert.Equal(10, shape.X);
        Assert.Equal(20, shape.Y);
        Assert.Equal(100, shape.Width);
        Assert.Equal(80, shape.Height);
        Assert.NotNull(shape.CustomPathData);
    }

    [Fact]
    public void BuildShapes_MultiplePaths_ShareOnePlacementButKeepRelativeAlignment()
    {
        // A 20x20 background square plus a nested 5,5-10,10 "glyph" square (a quarter of the
        // background's size, offset toward its top-left quadrant).
        var shapes = SvgImporter.BuildShapes(
            ["M0 0 L20 0 L20 20 L0 20 Z", "M5 5 L10 5 L10 10 L5 10 Z"],
            ArgbColor.FromRgb(0, 0, 0), ArgbColor.FromRgb(0, 0, 0), 0,
            placeX: 0, placeY: 0, placeWidth: 100, placeHeight: 100);

        Assert.Equal(2, shapes.Count);

        var background = shapes[0];
        var glyph = shapes[1];

        // Both shapes render into the SAME 100x100 placement box.
        Assert.Equal(background.X, glyph.X);
        Assert.Equal(background.Width, glyph.Width);

        using var backgroundPath = ShapeGeometryFactory.Build(background);
        using var glyphPath = ShapeGeometryFactory.Build(glyph);

        // The glyph is a quarter-sized square nested in the background's top-left quadrant, so its
        // rendered bounds must be a strict, smaller sub-rectangle of the background's — NOT
        // stretched to fill the same 100x100 box independently.
        Assert.True(glyphPath.Bounds.Width < backgroundPath.Bounds.Width * 0.5f);
        Assert.True(glyphPath.Bounds.Height < backgroundPath.Bounds.Height * 0.5f);
        Assert.True(glyphPath.Bounds.Left > backgroundPath.Bounds.Left);
        Assert.True(glyphPath.Bounds.Top > backgroundPath.Bounds.Top);
    }

    [Fact]
    public void BuildShapes_WithNoParseablePaths_ReturnsEmpty()
    {
        var shapes = SvgImporter.BuildShapes(
            ["not valid svg path data"],
            ArgbColor.FromRgb(0, 0, 0), ArgbColor.FromRgb(0, 0, 0), 0,
            placeX: 0, placeY: 0, placeWidth: 100, placeHeight: 100);

        Assert.Empty(shapes);
    }
}
