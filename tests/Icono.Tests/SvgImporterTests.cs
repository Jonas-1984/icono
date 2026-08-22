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
    public void ExtractPathData_ConvertsCircleEllipseAndRectElementsToPathData()
    {
        const string svg = """
            <svg viewBox="0 0 24 24">
              <path d="M2 2 L8 2 L8 8 Z" />
              <circle cx="9" cy="9" r="2" />
              <ellipse cx="12" cy="5" rx="9" ry="3" />
              <rect x="14" y="2" width="8" height="8" rx="1" />
            </svg>
            """;

        var result = SvgImporter.ExtractPathData(svg);

        Assert.Equal(4, result.Count);
        Assert.Equal("M2 2 L8 2 L8 8 Z", result[0]);
        // Every non-path element must parse as valid SVG path data (and be non-degenerate) —
        // exact formatting isn't asserted, just that a shared bounding box can be computed from it.
        foreach (var d in result.Skip(1))
        {
            using var parsed = SkiaSharp.SKPath.ParseSvgPathData(d);
            Assert.NotNull(parsed);
            Assert.True(parsed!.Bounds.Width > 0);
            Assert.True(parsed.Bounds.Height > 0);
        }
    }

    [Fact]
    public void IsStrokeStyleSvg_DetectsFillNoneRegardlessOfQuoteStyle()
    {
        Assert.True(SvgImporter.IsStrokeStyleSvg("<svg fill=\"none\" stroke=\"currentColor\"><path d=\"M0 0\"/></svg>"));
        Assert.True(SvgImporter.IsStrokeStyleSvg("<svg fill='none'><path d=\"M0 0\"/></svg>"));
        Assert.False(SvgImporter.IsStrokeStyleSvg("<svg><path d=\"M0 0 Z\" fill=\"#000\"/></svg>"));
    }

    [Fact]
    public void BuildShapes_StrokeOnlyTrue_MarksEveryShapeAsCustomPathStrokeOnly()
    {
        var shapes = SvgImporter.BuildShapes(
            ["M0 0 L20 0 L20 20 L0 20 Z"],
            ArgbColor.FromRgb(1, 2, 3), ArgbColor.FromRgb(4, 5, 6), 2,
            placeX: 0, placeY: 0, placeWidth: 80, placeHeight: 80, strokeOnly: true);

        var shape = Assert.Single(shapes);
        Assert.True(shape.CustomPathStrokeOnly);
    }

    [Fact]
    public void BuildShapes_StrokeOnlyDefaultsToFalse_ForShapeCombinerResults()
    {
        var shapes = SvgImporter.BuildShapes(
            ["M0 0 L20 0 L20 20 L0 20 Z"],
            ArgbColor.FromRgb(1, 2, 3), ArgbColor.FromRgb(4, 5, 6), 0,
            placeX: 0, placeY: 0, placeWidth: 80, placeHeight: 80);

        var shape = Assert.Single(shapes);
        Assert.False(shape.CustomPathStrokeOnly);
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
    public void MeasureBounds_ReturnsTheUnionBoundingBoxOfEveryPath()
    {
        var result = SvgImporter.MeasureBounds(["M0 0 L40 0 L40 10 L0 10 Z", "M10 -5 L20 -5 L20 5 L10 5 Z"]);

        Assert.NotNull(result);
        // Union of a 40x10 box at y=[0,10] and a 10x10 box at y=[-5,5]: x spans [0,40], y spans [-5,10].
        Assert.Equal(40, result!.Value.Width, 3);
        Assert.Equal(15, result.Value.Height, 3);
    }

    [Fact]
    public void MeasureBounds_WithNoParseablePaths_ReturnsNull()
    {
        Assert.Null(SvgImporter.MeasureBounds(["not valid svg path data"]));
    }

    [Fact]
    public void MeasureBounds_WithEmptyInput_ReturnsNull()
    {
        Assert.Null(SvgImporter.MeasureBounds([]));
    }

    [Fact]
    public void BuildShapes_GivenAspectCorrectPlacement_PreservesTheSourcesOwnProportions()
    {
        // A wide 4:1 source rectangle (40x10) — the real-world bug this guards against: importing
        // it into a fixed SQUARE placement box used to squash it into a square. A caller that first
        // measures the source and derives a proportional placement box (as IconLibraryView now
        // does) must get an undistorted result back out of BuildShapes.
        const string wideRect = "M0 0 L40 0 L40 10 L0 10 Z";
        var measured = SvgImporter.MeasureBounds([wideRect]);
        Assert.NotNull(measured);

        const double placeSize = 80;
        var scale = placeSize / Math.Max(measured!.Value.Width, measured.Value.Height);
        var placeWidth = measured.Value.Width * scale;
        var placeHeight = measured.Value.Height * scale;

        var shapes = SvgImporter.BuildShapes(
            [wideRect], ArgbColor.FromRgb(0, 0, 0), ArgbColor.FromRgb(0, 0, 0), 0,
            placeX: 0, placeY: 0, placeWidth: placeWidth, placeHeight: placeHeight);

        var shape = Assert.Single(shapes);
        using var built = ShapeGeometryFactory.Build(shape);

        // The source is 4:1 (width:height) — the rendered result must keep that ratio, not come
        // out square (the pre-fix bug) or any other ratio.
        var renderedRatio = built.Bounds.Width / built.Bounds.Height;
        Assert.InRange(renderedRatio, 3.9, 4.1);
    }

    [Fact]
    public void ExtractElements_ReadsEachElementsOwnHexFillColor()
    {
        const string svg = """
            <svg viewBox="0 0 24 24">
              <path fill="#0065A9" d="M0 0 L10 0 L10 10 Z" />
              <path fill="#007ACC" d="M2 2 L8 2 L8 8 Z" />
            </svg>
            """;

        var elements = SvgImporter.ExtractElements(svg);

        Assert.Equal(2, elements.Count);
        Assert.Equal(ArgbColor.FromRgb(0x00, 0x65, 0xA9), elements[0].Fill);
        Assert.Equal(ArgbColor.FromRgb(0x00, 0x7A, 0xCC), elements[1].Fill);
    }

    [Fact]
    public void ExtractElements_ParsesRgbFunctionAndNamedColors()
    {
        const string svg = """
            <svg viewBox="0 0 24 24">
              <path fill="rgb(255, 0, 0)" d="M0 0 L10 0 L10 10 Z" />
              <path fill="blue" d="M2 2 L8 2 L8 8 Z" />
            </svg>
            """;

        var elements = SvgImporter.ExtractElements(svg);

        Assert.Equal(ArgbColor.FromRgb(255, 0, 0), elements[0].Fill);
        Assert.Equal(ArgbColor.FromRgb(0, 0, 255), elements[1].Fill);
    }

    [Fact]
    public void ExtractElements_TreatsNoneCurrentColorAndMissingFillAsUnspecified()
    {
        const string svg = """
            <svg fill="none" stroke="currentColor">
              <path fill="none" d="M0 0 L10 0 L10 10 Z" />
              <path fill="currentColor" d="M2 2 L8 2 L8 8 Z" />
              <path d="M3 3 L6 3 L6 6 Z" />
            </svg>
            """;

        var elements = SvgImporter.ExtractElements(svg);

        Assert.All(elements, e => Assert.Null(e.Fill));
    }

    [Fact]
    public void ExtractElements_ReadsFillFromAStyleAttribute()
    {
        const string svg = """<path style="fill:#ff0000; stroke:none" d="M0 0 L10 0 L10 10 Z" />""";

        var elements = SvgImporter.ExtractElements(svg);

        Assert.Equal(ArgbColor.FromRgb(255, 0, 0), Assert.Single(elements).Fill);
    }

    [Fact]
    public void BuildShapes_WithElements_EachShapeKeepsItsOwnFillColor()
    {
        // The exact bug this guards against: a two-tone logo (two pieces, two distinct fills)
        // must import as two differently-colored shapes, not get flattened to one uniform color.
        IReadOnlyList<SvgImporter.SvgElement> elements =
        [
            new("M0 0 L20 0 L20 20 L0 20 Z", ArgbColor.FromRgb(0, 100, 170)),
            new("M5 5 L10 5 L10 10 L5 10 Z", ArgbColor.FromRgb(0, 120, 200)),
        ];

        var shapes = SvgImporter.BuildShapes(
            elements, ArgbColor.FromRgb(0, 0, 0), ArgbColor.FromRgb(0, 0, 0), 0,
            placeX: 0, placeY: 0, placeWidth: 100, placeHeight: 100);

        Assert.Equal(2, shapes.Count);
        Assert.Equal(ArgbColor.FromRgb(0, 100, 170), shapes[0].FillColor);
        Assert.Equal(ArgbColor.FromRgb(0, 120, 200), shapes[1].FillColor);
    }

    [Fact]
    public void BuildShapes_WithElements_UnspecifiedFillFallsBackToTheDefaultColor()
    {
        IReadOnlyList<SvgImporter.SvgElement> elements = [new("M0 0 L20 0 L20 20 L0 20 Z", null)];

        var shapes = SvgImporter.BuildShapes(
            elements, ArgbColor.FromRgb(9, 9, 9), ArgbColor.FromRgb(0, 0, 0), 0,
            placeX: 0, placeY: 0, placeWidth: 100, placeHeight: 100);

        Assert.Equal(ArgbColor.FromRgb(9, 9, 9), Assert.Single(shapes).FillColor);
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
