using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class WallSnapTests
{
    private static Document EmptyDocument()
    {
        var document = new Document { Width = 800, Height = 600 };
        document.Layers.Add(new Layer { Name = "L" });
        return document;
    }

    private static Shape Wall(params (double X, double Y)[] points)
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = points[0].X, Y = points[0].Y, WallThickness = 10 };
        wall.Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList();
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static WallSnapOptions Only(Action<WallSnapOptions> configure)
    {
        var options = new WallSnapOptions
        {
            Axis = false, Align = false, EqualLength = false, Parallel = false,
        };
        configure(options);
        return options;
    }

    [Fact]
    public void Axis_SnapsToHorizontalAndVertical_FromThePreviousPoint()
    {
        var document = EmptyDocument();
        var previous = new SKPoint(100, 100);

        var horizontal = WallSnapper.Snap(new SKPoint(250, 104), previous, [previous], document, Only(o => o.Axis = true), 8);
        var vertical = WallSnapper.Snap(new SKPoint(103, 300), previous, [previous], document, Only(o => o.Axis = true), 8);
        var diagonal = WallSnapper.Snap(new SKPoint(250, 190), previous, [previous], document, Only(o => o.Axis = true), 8);

        Assert.True(horizontal.Snapped);
        Assert.Equal(100, horizontal.Point.Y, 3);
        Assert.Equal(250, horizontal.Point.X, 3);
        Assert.Equal(100, vertical.Point.X, 3);
        Assert.False(diagonal.Snapped);
    }

    [Fact]
    public void Shift_ForcesTheAxisWhateverTheDistance()
    {
        var previous = new SKPoint(0, 0);

        var result = WallSnapper.Snap(new SKPoint(100, 60), previous, [previous], EmptyDocument(), new WallSnapOptions { Enabled = false }, 8, forceAxis: true);

        Assert.Equal(0, result.Point.Y, 3);
        Assert.Equal(100, result.Point.X, 3);
    }

    [Fact]
    public void Align_PutsFacingWallsExactlyOppositeEachOther()
    {
        var document = EmptyDocument();
        document.Layers[0].Shapes.Add(Wall((100, 100), (100, 400))); // a wall on the left
        var previous = new SKPoint(300, 100);

        // drawing the facing wall downwards: with the axis locked, its end lines up (same y) with the end of the left wall
        var options = Only(o => { o.Axis = true; o.Align = true; });
        var result = WallSnapper.Snap(new SKPoint(302, 396), previous, [previous], document, options, 8);

        Assert.True(result.Snapped);
        Assert.Equal(300, result.Point.X, 3);
        Assert.Equal(400, result.Point.Y, 3);
        Assert.Contains(result.Guides, g => g.Kind == SnapKind.Align);
    }

    [Fact]
    public void Align_WithoutAPreviousPoint_LinesUpTheStartOfTheWall()
    {
        var document = EmptyDocument();
        document.Layers[0].Shapes.Add(Wall((100, 100), (400, 100)));

        var result = WallSnapper.Snap(new SKPoint(250, 106), null, [], document, Only(o => o.Align = true), 8);

        Assert.True(result.Snapped);
        Assert.Equal(100, result.Point.Y, 3);
        Assert.Equal(250, result.Point.X, 3);
    }

    [Fact]
    public void Align_PutsTheFaceOfTheNewWallFlushWithTheFaceOfAnotherWall()
    {
        var document = EmptyDocument();
        document.Layers[0].Shapes.Add(Wall((100, 100), (400, 100))); // thickness 10: faces at y = 95 and 105

        // a thicker wall of 20: its centre line at 85 has its lower face on 95 (stacked), at 115 its upper face on 105
        var stacked = WallSnapper.Snap(new SKPoint(550, 112), null, [], document, Only(o => o.Align = true), 8, wallThickness: 20);
        // the same thickness: its centre line on 100 puts both walls in one row
        var inRow = WallSnapper.Snap(new SKPoint(550, 103), null, [], document, Only(o => o.Align = true), 8, wallThickness: 10);

        Assert.True(stacked.Snapped);
        Assert.Equal(115, stacked.Point.Y, 3);
        Assert.True(inRow.Snapped);
        Assert.Equal(100, inRow.Point.Y, 3);
        Assert.Contains(inRow.Guides, g => g.Kind == SnapKind.Align);
    }

    [Fact]
    public void Align_WithoutAThickness_UsesTheCentreLineOnly()
    {
        var document = EmptyDocument();
        document.Layers[0].Shapes.Add(Wall((100, 100), (400, 100)));

        var result = WallSnapper.Snap(new SKPoint(550, 106), null, [], document, Only(o => o.Align = true), 8);

        Assert.Equal(100, result.Point.Y, 3);
    }

    [Fact]
    public void EqualLength_SnapsToTheLengthOfAnotherSegment()
    {
        var document = EmptyDocument();
        document.Layers[0].Shapes.Add(Wall((100, 500), (400, 500))); // 300 long
        var previous = new SKPoint(100, 100);

        var options = Only(o => { o.Axis = true; o.EqualLength = true; });
        var result = WallSnapper.Snap(new SKPoint(396, 103), previous, [previous], document, options, 8);

        Assert.True(result.Snapped);
        Assert.Equal(400, result.Point.X, 3); // 300 from the start
        Assert.Equal(100, result.Point.Y, 3);
        Assert.Contains(result.Guides, g => g.Kind == SnapKind.EqualLength);
    }

    [Fact]
    public void EqualLength_AlsoMatchesTheSegmentJustDrawnInTheSameWall()
    {
        var a = new SKPoint(100, 100);
        var b = new SKPoint(300, 100); // 200
        var options = Only(o => { o.Axis = true; o.EqualLength = true; });

        var result = WallSnapper.Snap(new SKPoint(303, 296), b, [a, b], EmptyDocument(), options, 8);

        Assert.Equal(300, result.Point.X, 3);
        Assert.Equal(300, result.Point.Y, 3); // 200 down, like the first segment
    }

    [Fact]
    public void Parallel_FollowsTheDirectionOfAnExistingWall_AndItsPerpendicular()
    {
        var document = EmptyDocument();
        document.Layers[0].Shapes.Add(Wall((0, 0), (200, 200))); // 45 degrees
        var previous = new SKPoint(400, 100);
        var options = Only(o => o.Parallel = true);

        var parallel = WallSnapper.Snap(new SKPoint(520, 222), previous, [previous], document, options, 8);
        var perpendicular = WallSnapper.Snap(new SKPoint(520, -22), previous, [previous], document, options, 8);

        Assert.True(parallel.Snapped);
        Assert.Equal(parallel.Point.X - 400, parallel.Point.Y - 100, 2);
        Assert.True(perpendicular.Snapped);
        Assert.Equal(-(perpendicular.Point.X - 400), perpendicular.Point.Y - 100, 2);
    }

    [Fact]
    public void Disabled_SnapsToNothing()
    {
        var document = EmptyDocument();
        document.Layers[0].Shapes.Add(Wall((100, 100), (300, 100)));
        var previous = new SKPoint(100, 300);

        var result = WallSnapper.Snap(new SKPoint(102, 101), previous, [previous], document, new WallSnapOptions { Enabled = false }, 8);

        Assert.False(result.Snapped);
        Assert.Equal(102, result.Point.X, 3);
    }

    [Fact]
    public void TheLabelNamesLengthAndAngle()
    {
        var previous = new SKPoint(0, 0);

        var result = WallSnapper.Snap(new SKPoint(96, 0), previous, [previous], EmptyDocument(), Only(o => o.Axis = true), 8);

        Assert.StartsWith("2.54 cm", result.Label);
        Assert.Contains("0°", result.Label);
    }
}
