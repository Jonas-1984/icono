using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class WallStraightenTests
{
    /// <summary>A room whose top is a gable: the corner (250, 50) is the peak between (100, 120) and (400, 120).</summary>
    private static Shape GableRoom()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 50, WallClosed = true, WallThickness = 10,
            Points = [new CorePoint(0, 70), new CorePoint(150, 0), new CorePoint(300, 70), new CorePoint(300, 400), new CorePoint(0, 400)],
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static List<SKPoint> Abs(Shape wall) => WallGeometry.AbsolutePoints(wall);

    [Fact]
    public void StraighteningTheGablePeak_MovesItOntoTheLineBetweenItsNeighbours()
    {
        var wall = GableRoom();

        Assert.True(WallStraightener.StraightenCorner(wall, 1));

        var p = Abs(wall);
        Assert.Equal(120, p[1].Y, 3); // the peak is on the line from (100,120) to (400,120)
        Assert.Equal(250, p[1].X, 3);
        Assert.Equal(0, WallStraightener.TurnDegrees(p[0], p[1], p[2]), 3);
    }

    [Fact]
    public void ThenTheTwoPartsMergeIntoOneWall()
    {
        var wall = GableRoom();
        WallStraightener.StraightenCorner(wall, 1);

        var removed = WallStraightener.MergeStraightParts(wall);

        Assert.Equal(1, removed);
        Assert.Equal(4, wall.Points!.Count);
        Assert.Equal(4, WallGeometry.Segments(wall).Count);
        Assert.Equal(300, WallGeometry.Segments(wall)[0].To.X - WallGeometry.Segments(wall)[0].From.X, 3);
    }

    [Fact]
    public void AWallThatIsNotStraight_IsLeftAlone()
    {
        var wall = GableRoom();

        Assert.Equal(0, WallStraightener.MergeStraightParts(wall));
        Assert.Equal(5, wall.Points!.Count);
    }

    [Fact]
    public void AlmostStraightWalls_MergeWithinTheTolerance()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 0, Y = 0, WallThickness = 10,
            Points = [new CorePoint(0, 0), new CorePoint(100, 0.5), new CorePoint(200, 0)], // 0.3 degrees of turn
        };

        Assert.Equal(1, WallStraightener.MergeStraightParts(wall, 1.0));
        Assert.Equal(2, wall.Points!.Count);
    }

    [Fact]
    public void TheEndsOfAnOpenWall_AreNeverRemoved_AndARingKeepsThreeCorners()
    {
        var open = new Shape { Kind = ShapeKind.Wall, X = 0, Y = 0, Points = [new CorePoint(0, 0), new CorePoint(100, 0)] };
        Assert.Equal(0, WallStraightener.MergeStraightParts(open));
        Assert.False(WallStraightener.StraightenCorner(open, 0));

        var ring = new Shape
        {
            Kind = ShapeKind.Wall, X = 0, Y = 0, WallClosed = true,
            Points = [new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(200, 0)], // flat: all three in one line
        };
        WallStraightener.MergeStraightParts(ring);
        Assert.True(ring.Points!.Count >= 3);
    }

    [Fact]
    public void RemovingACorner_KeepsTheSettingsOfTheOtherDimensions()
    {
        var wall = GableRoom();
        WallStraightener.StraightenCorner(wall, 1);
        wall.WallDimensionOverrides = [new WallDimensionOverride(3, Hidden: true)]; // the right-hand wall

        WallStraightener.MergeStraightParts(wall);

        // one segment less in front of it: the setting now belongs to segment 2
        Assert.Equal(2, wall.WallDimensionOverrides![0].Segment);
    }

    [Fact]
    public void DraggingTheGablePeakNearTheStraightLine_SnapsOntoIt()
    {
        var wall = GableRoom();
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);

        // the peak is dragged to (252, 116): 4 px above the line y = 120
        var snap = WallStraightener.Snap(document, wall, 1, new SKPoint(252, 116), 8);

        Assert.True(snap.Snapped);
        Assert.Equal(120, snap.Point.Y, 3);
        Assert.Contains(snap.Guides, g => g.Straight);
    }

    [Fact]
    public void FarFromTheLine_ACornerStillLinesUpWithItsNeighboursAndOtherWalls()
    {
        var wall = GableRoom();
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);

        // dragged to x = 103: near the x of its neighbour at 100 -> lines up vertically, but is far from the straight line
        var snap = WallStraightener.Snap(document, wall, 1, new SKPoint(103, 60), 8);

        Assert.Equal(100, snap.Point.X, 3);
        Assert.Equal(60, snap.Point.Y, 3);
        Assert.DoesNotContain(snap.Guides, g => g.Straight);
    }

    [Fact]
    public void NothingNearby_NothingSnaps()
    {
        var wall = GableRoom();
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);

        var snap = WallStraightener.Snap(document, wall, 1, new SKPoint(250, 70), 8);

        Assert.False(snap.Snapped);
        Assert.Equal(new SKPoint(250, 70), snap.Point);
    }
}
