using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class ObjectSnapTests
{
    private static readonly ObjectSnapOptions On = new();

    /// <summary>A room whose wall centre lines run 100..500 x 100..400, 20 px thick: the inner faces are at 110 / 490 and 110 / 390.</summary>
    private static Document RoomDocument(out Shape wall)
    {
        wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallClosed = true, WallThickness = 20,
            Points = [new CorePoint(0, 0), new CorePoint(400, 0), new CorePoint(400, 300), new CorePoint(0, 300)],
        };
        PathBoundsCalculator.Recompute(wall);
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);
        return document;
    }

    [Fact]
    public void ACabinetNearAWall_SnapsItsEdgeToTheInnerFace()
    {
        var document = RoomDocument(out _);

        // 80 x 60 cabinet whose left edge is 4 px from the inner face (x = 110)
        var result = ObjectSnapper.Snap(document, (114, 200, 80, 60), [], 8, On);

        Assert.Equal(-4, result.Dx, 6);
        Assert.Equal(0, result.Dy, 6);
        Assert.Contains(result.Guides, g => g.Vertical && g.Kind == ObjectSnapKind.Wall && Math.Abs(g.Position - 110) < 1e-6);
    }

    [Fact]
    public void ACabinetNearACorner_SnapsBothWays_AndSitsInTheCorner()
    {
        var document = RoomDocument(out _);

        var result = ObjectSnapper.Snap(document, (113, 105 + 4, 80, 60), [], 8, On); // 3 px off the left face, 1 px off the top face

        Assert.Equal(-3, result.Dx, 6);
        Assert.Equal(110 - 109, result.Dy, 6);
        Assert.Equal(2, result.Guides.Count);
    }

    [Fact]
    public void TheFarEdgeSnapsToo_SoACabinetCanSitAgainstTheOppositeWall()
    {
        var document = RoomDocument(out _);

        var result = ObjectSnapper.Snap(document, (400, 300, 80, 60), [], 8, On); // right edge 480 is 10 from 490: out of reach
        Assert.Equal(0, result.Dx, 6);

        result = ObjectSnapper.Snap(document, (405, 300, 80, 60), [], 8, On); // right edge 485, 5 from the face at 490
        Assert.Equal(5, result.Dx, 6);
    }

    [Fact]
    public void FarFromEverything_NothingSnaps()
    {
        var document = RoomDocument(out _);

        var result = ObjectSnapper.Snap(document, (250, 200, 80, 60), [], 8, On);

        Assert.False(result.Snapped);
        Assert.Equal((0, 0), (result.Dx, result.Dy));
    }

    [Fact]
    public void ShapesLineUpWithEachOther_EdgesAndCentres()
    {
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        var other = new Shape { Kind = ShapeKind.Rectangle };
        ShapeOutline.SetBox(other, 200, 100, 100, 50); // the drawn outline, not the frame
        layer.Shapes.Add(other);
        document.Layers.Add(layer);

        // right edge of the moving 60 x 40 shape (at 296) is 4 px from the other's right edge (300)
        var edge = ObjectSnapper.Snap(document, (236, 300, 60, 40), [], 8, On);
        Assert.Equal(4, edge.Dx, 6);

        // its centre (at 245) is 5 px from the other's centre (250)
        var centre = ObjectSnapper.Snap(document, (215, 300, 60, 40), [], 8, On);
        Assert.Equal(5, centre.Dx, 6);
        Assert.All(centre.Guides, g => Assert.Equal(ObjectSnapKind.Object, g.Kind));
    }

    [Fact]
    public void TheShapesThatMoveAreNotTargets()
    {
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        var moving = new Shape { Kind = ShapeKind.Rectangle };
        ShapeOutline.SetBox(moving, 200, 100, 100, 50);
        layer.Shapes.Add(moving);
        document.Layers.Add(layer);

        var result = ObjectSnapper.Snap(document, (203, 102, 100, 50), [moving], 8, On);

        Assert.False(result.Snapped);
    }

    [Fact]
    public void TheOptionsSwitchTheKindsOfTargetsOnAndOff()
    {
        var document = RoomDocument(out _);

        Assert.False(ObjectSnapper.Snap(document, (114, 200, 80, 60), [], 8, new ObjectSnapOptions { Enabled = false }).Snapped);
        Assert.False(ObjectSnapper.Snap(document, (114, 200, 80, 60), [], 8, new ObjectSnapOptions { Walls = false }).Snapped);
        Assert.True(ObjectSnapper.Snap(document, (114, 200, 80, 60), [], 8, new ObjectSnapOptions { Objects = false }).Snapped);
    }

    [Fact]
    public void APoint_SnapsToTheWallFaces_WhenPlacingOrResizing()
    {
        var document = RoomDocument(out _);

        var result = ObjectSnapper.Snap(document, (493, 250, 0, 0), [], 8, On); // a point 3 px from the inner face at x = 490

        Assert.Equal(-3, result.Dx, 6);
    }

    [Fact]
    public void ALineOfAWallOnlyAttractsWhereTheFrameIsAlongIt()
    {
        var document = RoomDocument(out _);

        // x = 110 is the left inner face, but it only runs from y = 110 to 390: a frame far below is not attracted
        var result = ObjectSnapper.Snap(document, (114, 520, 80, 40), [], 8, On);

        Assert.False(result.Snapped);
    }
}
