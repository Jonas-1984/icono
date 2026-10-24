using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class ShapeArrangerTests
{
    private static Shape Box(double x, double y, double w, double h) => new() { Kind = ShapeKind.Rectangle, X = x, Y = y, Width = w, Height = h };

    [Fact]
    public void Several_Shapes_AlignToTheFrameAroundThem()
    {
        var a = Box(10, 10, 20, 20);
        var b = Box(100, 50, 40, 10);

        ShapeArranger.Align([a, b], AlignKind.Left, 500, 500);
        Assert.Equal((10, 10), (a.X, b.X));

        ShapeArranger.Align([a, b], AlignKind.Right, 500, 500);
        Assert.Equal(50, a.X + a.Width); // after the left alignment the frame is 10..50 wide
        Assert.Equal(50, b.X + b.Width);

        ShapeArranger.Align([a, b], AlignKind.CenterVertically, 500, 500);
        Assert.Equal(a.Y + a.Height / 2, b.Y + b.Height / 2, 6);
        Assert.Equal(35, a.Y + a.Height / 2, 6); // the middle of the frame 10..60

        ShapeArranger.Align([a, b], AlignKind.Bottom, 500, 500);
        Assert.Equal(a.Y + a.Height, b.Y + b.Height, 6);
    }

    [Fact]
    public void ASingleShape_AlignsToThePage()
    {
        var a = Box(40, 40, 100, 50);

        ShapeArranger.Align([a], AlignKind.CenterHorizontally, 400, 300);
        Assert.Equal(150, a.X);

        ShapeArranger.Align([a], AlignKind.Bottom, 400, 300);
        Assert.Equal(250, a.Y);

        ShapeArranger.Align([a], AlignKind.Left, 400, 300);
        Assert.Equal(0, a.X);
    }

    [Fact]
    public void Align_ReportsOnlyTheShapesThatMoved_AndKeepsTheirSize()
    {
        var a = Box(10, 10, 20, 20);
        var b = Box(10, 80, 40, 10);

        var moved = ShapeArranger.Align([a, b], AlignKind.Left, 500, 500);

        Assert.Empty(moved);
        Assert.Equal((20, 20), (a.Width, a.Height));
    }

    [Fact]
    public void Distribute_MakesTheGapsEqual_AndKeepsTheOutermostShapes()
    {
        var a = Box(0, 0, 10, 10);
        var b = Box(15, 0, 20, 10);
        var c = Box(100, 0, 10, 10);

        var moved = ShapeArranger.Distribute([c, a, b], horizontally: true);

        Assert.Equal(0, a.X);
        Assert.Equal(110, c.X + c.Width);
        // 110 - 40 of shapes = 70 of gaps, two gaps of 35
        Assert.Equal(45, b.X, 6);
        Assert.Equal([b], moved);
    }

    [Fact]
    public void Distribute_NeedsThreeShapes_AndWorksVertically()
    {
        Assert.Empty(ShapeArranger.Distribute([Box(0, 0, 5, 5), Box(0, 50, 5, 5)], horizontally: false));

        var a = Box(0, 0, 10, 10);
        var b = Box(0, 20, 10, 10);
        var c = Box(0, 90, 10, 10);

        ShapeArranger.Distribute([a, b, c], horizontally: false);

        Assert.Equal(45, b.Y, 6); // (100 - 30) / 2 = 35 gap -> 10 + 35
    }
}
