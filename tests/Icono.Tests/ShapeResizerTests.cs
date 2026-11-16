using Icono.Core;
using Icono.Editor;

namespace Icono.Tests;

public class ShapeResizerTests
{
    private static void Resize(Shape shape, ResizeHandle handle, (double X, double Y) from, (double X, double Y) to, bool keepProportions)
    {
        var frame = ShapeResizer.ResizeFrame(
            handle, ShapeResizer.Outline(shape), (shape.X, shape.Y, shape.Width, shape.Height), from, to,
            shape.Rotation, ShapeResizer.Pivot(shape), keepProportions);
        (shape.X, shape.Y, shape.Width, shape.Height) = frame;
    }

    [Fact]
    public void DraggingTheRightEdge_StretchesOnlyTheWidth()
    {
        var shape = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 100, Height = 50 };
        var before = ShapeResizer.Outline(shape);
        var handle = ShapeResizer.Handles(shape).Single(h => h.Handle == ResizeHandle.Right);

        Resize(shape, ResizeHandle.Right, (handle.X, handle.Y), (handle.X + 40, handle.Y + 30), keepProportions: true);

        var after = ShapeResizer.Outline(shape);
        Assert.Equal(before.W + 40, after.W, 3);
        Assert.Equal(before.H, after.H, 3);
        Assert.Equal(before.X, after.X, 3);
    }

    [Fact]
    public void DraggingACorner_KeepsTheProportions_AnchoredAtTheOppositeCorner()
    {
        var shape = new Shape { Kind = ShapeKind.Rectangle, X = 10, Y = 10, Width = 100, Height = 50 };
        var before = ShapeResizer.Outline(shape);
        var corner = ShapeResizer.Handles(shape).Single(h => h.Handle == ResizeHandle.BottomRight);

        Resize(shape, ResizeHandle.BottomRight, (corner.X, corner.Y), (corner.X + before.W, corner.Y), keepProportions: true);

        var after = ShapeResizer.Outline(shape);
        Assert.Equal(before.W * 2, after.W, 3);
        Assert.Equal(before.H * 2, after.H, 3);
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
    }

    [Fact]
    public void ATurnedShape_IsResizedAlongItsOwnSides()
    {
        // turned by 90 degrees: its "right" edge now points down on the page
        var shape = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 100, Height = 40, Rotation = 90 };
        var before = ShapeResizer.Outline(shape);
        var handle = ShapeResizer.Handles(shape).Single(h => h.Handle == ResizeHandle.Right);

        Resize(shape, ResizeHandle.Right, (handle.X, handle.Y), (handle.X, handle.Y + 30), keepProportions: false);

        var after = ShapeResizer.Outline(shape);
        Assert.Equal(before.W + 30, after.W, 3);
        Assert.Equal(before.H, after.H, 3);
    }

    [Fact]
    public void AGroup_ResizesAsOneUnit()
    {
        var a = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 50, Height = 50 };
        var b = new Shape { Kind = ShapeKind.Rectangle, X = 50, Y = 0, Width = 50, Height = 50 };
        var shapes = new[] { a, b };
        var bounds = ShapeResizer.GroupBounds(shapes);
        var starts = shapes.Select(s => (s.X, s.Y, s.Width, s.Height)).ToList();
        var corner = ShapeResizer.BoxHandles(bounds).Single(h => h.Handle == ResizeHandle.BottomRight);

        ShapeResizer.ResizeGroup(shapes, starts, ResizeHandle.BottomRight, bounds, (corner.X, corner.Y),
            (corner.X + bounds.W, corner.Y + bounds.H), keepProportions: true);

        // both doubled, b still right next to a
        Assert.Equal(100, a.Width, 3);
        Assert.Equal(100, b.Width, 3);
        Assert.Equal(a.X + a.Width, b.X, 3);
    }

    [Fact]
    public void RotateHandle_SitsAboveTheTopEdge_AndRotationTowardsItGivesTheCurrentTurn()
    {
        var shape = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 100, Height = 40, Rotation = 30 };
        var (x, y) = ShapeResizer.RotateHandle(shape, offset: 20);

        Assert.Equal(30, ShapeResizer.RotationTowards(shape, x, y), 3);
    }
}
