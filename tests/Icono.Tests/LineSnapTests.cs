using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>The snapping tools for lines and polylines: they are targets, they attract their own free end, and the wall helpers work for them.</summary>
public class LineSnapTests
{
    private static Document DocumentWith(params Shape[] shapes)
    {
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        foreach (var shape in shapes)
        {
            layer.Shapes.Add(shape);
        }

        document.Layers.Add(layer);
        return document;
    }

    private static Shape Polyline(params (double X, double Y)[] points)
    {
        var line = new Shape { Kind = ShapeKind.Polyline, X = points[0].X, Y = points[0].Y };
        line.Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList();
        PathBoundsCalculator.Recompute(line);
        return line;
    }

    private static PointSnapOptions Only(params PointSnapKind[] kinds)
    {
        var options = new PointSnapOptions();
        foreach (var kind in Enum.GetValues<PointSnapKind>())
        {
            options.Set(kind, kinds.Contains(kind));
        }

        return options;
    }

    [Fact]
    public void Nearest_IsOnByDefault_SoALineCanBeSnappedToAnywhereAlongIt()
    {
        var document = DocumentWith(Polyline((100, 100), (300, 100), (300, 300)));

        var result = PointSnapper.Snap(document, new SKPoint(210, 104), 8, new PointSnapOptions(), []);

        Assert.NotNull(result);
        Assert.Equal(PointSnapKind.Nearest, result!.Kind);
        Assert.Equal(100, result.Point.Y, 2);
    }

    [Fact]
    public void ThePartOfALineThatIsAlreadyDrawn_AttractsItsFreeEnd()
    {
        var document = DocumentWith();
        var drawn = Polyline((100, 100), (300, 100), (300, 300));

        // the free end comes back near the start of the line: it closes the figure
        var result = PointSnapper.Snap(document, new SKPoint(104, 103), 8, Only(PointSnapKind.Endpoint), [], new SKPoint(300, 300), [drawn]);

        Assert.Equal(PointSnapKind.Endpoint, result!.Kind);
        Assert.Equal(100, result.Point.X, 2);
        Assert.Equal(100, result.Point.Y, 2);
    }

    [Fact]
    public void ThePointThatIsDragged_DoesNotAttractItself()
    {
        var edited = Polyline((100, 100), (300, 100), (300, 300));
        var document = DocumentWith(edited);

        var result = PointSnapper.Snap(document, new SKPoint(302, 301), 8, Only(PointSnapKind.Endpoint, PointSnapKind.Nearest), [edited]);

        Assert.Null(result);
    }

    [Fact]
    public void WallHelpers_LineUpWithThePointsOfAPolyline()
    {
        var document = DocumentWith(Polyline((400, 100), (500, 300)));
        var options = new WallSnapOptions { Axis = false, Parallel = false, EqualLength = false, Align = true };

        // the free end of a line from (100, 250) lines up with x = 400 of the polyline's first point
        var result = WallSnapper.Snap(new SKPoint(403, 250), new SKPoint(100, 250), [new SKPoint(100, 250)], document, options, 8);

        Assert.True(result.Snapped);
        Assert.Equal(400, result.Point.X, 2);
    }

    [Fact]
    public void WallHelpers_TheEditedLineIsNotATargetForItself()
    {
        var edited = Polyline((400, 100), (500, 300));
        var document = DocumentWith(edited);
        var options = new WallSnapOptions { Axis = false, Parallel = false, EqualLength = false, Align = true };

        var result = WallSnapper.Snap(new SKPoint(403, 250), new SKPoint(100, 250), [new SKPoint(100, 250)], document, options, 8, exclude: edited);

        Assert.False(result.Snapped);
    }
}
