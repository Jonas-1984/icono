using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Tests;

public class SelectionAreaTests
{
    private static (Document Document, Shape Inside, Shape Straddling, Shape Outside) Sample()
    {
        var document = new Document { Width = 400, Height = 400 };
        var layer = new Layer { Name = "Layer 1" };
        var inside = new Shape { Kind = ShapeKind.Rectangle, X = 20, Y = 20, Width = 40, Height = 40 };
        var straddling = new Shape { Kind = ShapeKind.Rectangle, X = 90, Y = 20, Width = 60, Height = 40 }; // sticks out of the area at the right
        var outside = new Shape { Kind = ShapeKind.Rectangle, X = 300, Y = 300, Width = 40, Height = 40 };
        layer.Shapes.Add(inside);
        layer.Shapes.Add(straddling);
        layer.Shapes.Add(outside);
        document.Layers.Add(layer);
        return (document, inside, straddling, outside);
    }

    private static readonly SKRect Area = new(0, 0, 120, 100);

    [Fact]
    public void AWindow_ChoosesOnlyWhatLiesCompletelyInside()
    {
        var (document, inside, _, _) = Sample();

        Assert.Equal([inside], SelectionArea.Find(document, Area, crossing: false));
    }

    [Fact]
    public void ACrossing_ChoosesEverythingTheAreaTouches()
    {
        var (document, inside, straddling, _) = Sample();

        var found = SelectionArea.Find(document, Area, crossing: true);

        Assert.Equal(2, found.Count);
        Assert.Contains(inside, found);
        Assert.Contains(straddling, found);
    }

    [Fact]
    public void HiddenLayersAreSkipped()
    {
        var (document, _, _, _) = Sample();
        document.Layers[0].Visible = false;

        Assert.Empty(SelectionArea.Find(document, Area, crossing: true));
    }

    [Fact]
    public void DraggingToTheLeftIsACrossing()
    {
        Assert.True(SelectionArea.IsCrossing(startX: 100, currentX: 40));
        Assert.False(SelectionArea.IsCrossing(startX: 40, currentX: 100));
    }
}
