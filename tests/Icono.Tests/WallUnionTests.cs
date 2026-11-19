using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Tests;

[Collection("Scale")]
public class WallUnionTests : IDisposable
{
    public void Dispose() => DrawingScale.Denominator = 1;

    private static Shape Wall(double x, double y, params (double X, double Y)[] corners)
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = x, Y = y, WallThickness = 14,
            Points = corners.Select(c => new CorePoint(c.X, c.Y)).ToList(),
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static EditorContext With(params Shape[] walls)
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "L" };
        foreach (var wall in walls)
        {
            layer.Shapes.Add(wall);
        }

        context.Document.Layers.Add(layer);
        context.SelectedShapes.Clear();
        foreach (var wall in walls)
        {
            context.SelectedShapes.Add(wall);
        }

        return context;
    }

    [Fact]
    public void TwoWallsEndToEnd_BecomeOneWallWithACorner()
    {
        var context = With(Wall(100, 100, (0, 0), (200, 0)), Wall(300, 100, (0, 0), (0, 300)));

        context.JoinSelectedWalls();

        var wall = Assert.Single(context.Document.Layers[0].Shapes);
        Assert.Equal(3, wall.Points!.Count);
        Assert.Equal(ShapeKind.Wall, wall.Kind);
    }

    [Fact]
    public void Joining_IsOneUndoStep()
    {
        var context = With(Wall(100, 100, (0, 0), (200, 0)), Wall(300, 100, (0, 0), (0, 300)));

        context.JoinSelectedWalls();
        context.Undo();

        Assert.Equal(2, context.Document.Layers[0].Shapes.Count);
    }

    [Fact]
    public void WallsInOneLine_BecomeOneStraightWall()
    {
        var context = With(Wall(100, 100, (0, 0), (200, 0)), Wall(300, 100, (0, 0), (150, 0)));

        context.JoinSelectedWalls();

        var wall = Assert.Single(context.Document.Layers[0].Shapes);
        Assert.Equal(2, wall.Points!.Count);
    }

    [Fact]
    public void ATJunction_CannotBeOneWall_AndNothingChanges()
    {
        // the partition ends on the side of the long wall, in the middle
        var outer = Wall(100, 100, (0, 0), (600, 0));
        var partition = Wall(400, 100, (0, 0), (0, 300));
        var context = With(outer, partition);
        string? message = null;
        context.StatusMessage += m => message = m;

        Assert.True(WallEditor.MeetInT(outer, partition));
        context.JoinSelectedWalls();

        Assert.Equal(2, context.Document.Layers[0].Shapes.Count);
        Assert.Contains("T", message);
    }

    [Fact]
    public void EndsThatTouch_AreNoTJunction()
    {
        Assert.False(WallEditor.MeetInT(Wall(100, 100, (0, 0), (200, 0)), Wall(300, 100, (0, 0), (0, 300))));
        // at a corner of the other wall: not a T either
        Assert.False(WallEditor.MeetInT(Wall(100, 100, (0, 0), (200, 0), (200, 100)), Wall(300, 200, (0, 0), (0, 300))));
    }

    [Fact]
    public void ARoomAndAPartitionOnItsSide_MeetInAT()
    {
        var room = Wall(100, 100, (0, 0), (400, 0), (400, 300), (0, 300));
        room.WallClosed = true;
        var partition = Wall(300, 100, (0, 0), (0, 300));

        Assert.True(WallEditor.MeetInT(room, partition));
    }
}
