using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class ShapeHitTesterTests
{
    private static (Document Document, Shape Back, Shape Front) TwoOverlappingRectangles()
    {
        var document = new Document { Width = 400, Height = 400 };
        var layer = new Layer { Name = "Layer 1" };
        var front = new Shape { Kind = ShapeKind.Rectangle, X = 50, Y = 50, Width = 100, Height = 100 };
        var back = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 120, Height = 120 };
        layer.Shapes.Add(front); // index 0 = front-most
        layer.Shapes.Add(back);
        document.Layers.Add(layer);
        return (document, back, front);
    }

    [Fact]
    public void Find_ReturnsTheFrontMostShapeUnderThePoint()
    {
        var (document, back, front) = TwoOverlappingRectangles();

        Assert.Same(front, ShapeHitTester.Find(document, 100, 100, 1)?.Shape);
        Assert.Same(back, ShapeHitTester.Find(document, 10, 10, 1)?.Shape);
        Assert.Null(ShapeHitTester.Find(document, 300, 300, 1));
    }

    [Fact]
    public void Find_SkipsHiddenLayers()
    {
        var (document, _, _) = TwoOverlappingRectangles();
        document.Layers[0].Visible = false;

        Assert.Null(ShapeHitTester.Find(document, 100, 100, 1));
    }

    [Fact]
    public void Find_HitsATurnedShapeWhereItIsDrawn()
    {
        var document = new Document { Width = 400, Height = 400 };
        var layer = new Layer { Name = "Layer 1" };
        // a long thin bar turned by 90 degrees about its centre (200, 200): drawn upright from y 100 to 300
        var bar = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 190, Width = 200, Height = 20, Rotation = 90 };
        layer.Shapes.Add(bar);
        document.Layers.Add(layer);

        Assert.Same(bar, ShapeHitTester.Find(document, 200, 120, 1)?.Shape);
        Assert.Null(ShapeHitTester.Find(document, 120, 200, 1));
    }
}
