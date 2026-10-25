using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class WallUniteTests
{
    private static Shape Wall(params (double X, double Y)[] points)
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = points[0].X, Y = points[0].Y, WallThickness = 12 };
        wall.Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList();
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static List<(double X, double Y)> Absolute(Shape wall) =>
        wall.Points!.Select(p => (wall.X + p.X, wall.Y + p.Y)).ToList();

    [Fact]
    public void TwoWallsInARowWithAGap_BecomeOneStraightWall()
    {
        var result = WallEditor.Join(Wall((0, 0), (100, 0)), Wall((150, 0), (300, 0)))!;

        Assert.Equal(50, result.Gap, 3);
        Assert.Equal(2, result.StraightCorners);
        Assert.Equal([(0.0, 0.0), (300.0, 0.0)], Absolute(result.Wall));
        Assert.False(result.Wall.WallClosed);
    }

    [Fact]
    public void TwoTouchingWalls_ShareTheirCorner()
    {
        var result = WallEditor.Join(Wall((0, 0), (100, 0)), Wall((100, 0), (100, 80)))!;

        Assert.Equal(0, result.Gap);
        Assert.Equal([(0.0, 0.0), (100.0, 0.0), (100.0, 80.0)], Absolute(result.Wall));
    }

    [Fact]
    public void TheNearestEndsAreJoined_WhateverTheDirectionOfTheWalls()
    {
        // the second wall is drawn the other way round: its LAST point is the one next to the first wall
        var result = WallEditor.Join(Wall((0, 0), (100, 0)), Wall((100, 80), (100, 0)))!;

        Assert.Equal([(0.0, 0.0), (100.0, 0.0), (100.0, 80.0)], Absolute(result.Wall));

        // and the first wall may be the one whose START is next to the other
        var other = WallEditor.Join(Wall((100, 0), (0, 0)), Wall((100, 80), (100, 0)))!;
        Assert.Equal(3, other.Wall.Points!.Count);
    }

    [Fact]
    public void TheNewWallKeepsTheLookOfTheFirstWall()
    {
        var first = Wall((0, 0), (100, 0));
        first.WallThickness = 24;

        var result = WallEditor.Join(first, Wall((100, 0), (100, 80)))!;

        Assert.Equal(24, result.Wall.WallThickness);
        Assert.NotSame(first, result.Wall);
    }

    [Fact]
    public void ARoomIsOpenedAtTheCornerNearestToTheOtherWall()
    {
        var room = Wall((0, 0), (100, 0), (100, 100), (0, 100));
        room.WallClosed = true;

        var result = WallEditor.Join(room, Wall((200, 0), (300, 0)))!;

        Assert.False(result.Wall.WallClosed);
        Assert.Equal(100, result.Gap, 3); // from the corner (100, 0) to (200, 0)
        var points = Absolute(result.Wall);
        // the run starts on the corner nearest to the other wall, goes round the room and on along the bridge to the other wall; the corner
        // where the room meets that wall is straight on, so it is no corner any more
        Assert.Equal([(100.0, 0.0), (100.0, 100.0), (0.0, 100.0), (0.0, 0.0), (300.0, 0.0)], points);
    }
}

public class WallCornerSnapTests
{
    private static Shape Wall(params (double X, double Y)[] points)
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = points[0].X, Y = points[0].Y, WallThickness = 16 };
        wall.Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList();
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static Document DocumentWith(params Shape[] walls)
    {
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        foreach (var wall in walls)
        {
            layer.Shapes.Add(wall);
        }

        document.Layers.Add(layer);
        return document;
    }

    [Fact]
    public void ADraggedEnd_SnapsOntoTheOtherEndOfTheSameWall()
    {
        var wall = Wall((100, 100), (300, 100), (300, 300), (100, 300));
        var document = DocumentWith(wall);

        var snap = WallStraightener.Snap(document, wall, 3, new SkiaSharp.SKPoint(103, 106), 8);

        Assert.True(snap.OnCorner);
        Assert.Equal(100, snap.Point.X, 3);
        Assert.Equal(100, snap.Point.Y, 3);
    }

    [Fact]
    public void ADraggedEnd_SnapsOntoACornerOfAnotherWall()
    {
        var wall = Wall((100, 100), (300, 100));
        var other = Wall((400, 200), (400, 400));
        var document = DocumentWith(wall, other);

        var snap = WallStraightener.Snap(document, wall, 1, new SkiaSharp.SKPoint(396, 205), 8);

        Assert.True(snap.OnCorner);
        Assert.Equal(400, snap.Point.X, 3);
        Assert.Equal(200, snap.Point.Y, 3);
    }

    [Fact]
    public void ACornerDoesNotSnapOntoItsOwnNeighbour()
    {
        var wall = Wall((100, 100), (300, 100), (300, 300));
        var document = DocumentWith(wall);

        // right next to the neighbouring corner (300, 100): that would collapse the wall, so it is no corner snap
        var snap = WallStraightener.Snap(document, wall, 2, new SkiaSharp.SKPoint(302, 104), 8);

        Assert.False(snap.OnCorner);
    }

    [Fact]
    public void TwoEndsOnEachOther_CloseTheWallIntoARoom()
    {
        var wall = Wall((100, 100), (300, 100), (300, 300), (100, 300), (100, 100));

        Assert.True(WallEditor.CloseWhenEndsMeet(wall));

        Assert.True(wall.WallClosed);
        Assert.Equal(4, wall.Points!.Count);
    }

    [Fact]
    public void EndsThatAreApart_LeaveTheWallOpen()
    {
        var wall = Wall((100, 100), (300, 100), (300, 300), (100, 300));

        Assert.False(WallEditor.CloseWhenEndsMeet(wall));
        Assert.False(wall.WallClosed);
    }

    [Fact]
    public void AnOpenWallAroundARoom_MeasuresTheInnerFace()
    {
        // three sides of a room: the inside of the top wall is shorter than its centre line, not longer
        var wall = Wall((0, 0), (200, 0), (200, 100), (0, 100));
        wall.WallDimensionMeasure = WallMeasure.Inside;

        var top = WallGeometry.MeasuredLength(wall, 0);

        Assert.True(top < 200, $"measured {top}");
    }
}
