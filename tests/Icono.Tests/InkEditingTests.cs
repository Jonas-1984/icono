using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Tests;

public class InkEditingTests
{
    private static readonly SKPoint[] Stroke =
        [new(10, 10), new(20, 14), new(32, 20), new(45, 22), new(60, 18), new(75, 12), new(90, 10)];

    private static (EditorContext Context, Layer Layer, List<string> Messages) NewContext()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        var messages = new List<string>();
        context.StatusMessage += messages.Add;
        return (context, layer, messages);
    }

    [Fact]
    public void AStroke_BecomesOneObject_InThePenColour_AndUndoRemovesIt()
    {
        var (context, layer, _) = NewContext();
        context.PenColor = ArgbColor.FromRgb(200, 30, 30);

        Assert.True(InkEditing.Commit(context, Stroke, zoom: 1));

        var shape = Assert.Single(layer.Shapes);
        Assert.True(shape.Freehand);
        Assert.Equal(ArgbColor.FromRgb(200, 30, 30), shape.BorderColor);

        context.Undo();
        Assert.Empty(layer.Shapes);
    }

    [Fact]
    public void WhenEveryLayerIsLocked_NothingIsDrawn_AndTheUserIsTold()
    {
        var (context, layer, messages) = NewContext();
        layer.Locked = true;

        Assert.False(InkEditing.Commit(context, Stroke, zoom: 1));

        Assert.Empty(layer.Shapes);
        Assert.Contains("All layers are locked.", messages);
    }

    [Fact]
    public void ATap_MakesADot_ButNoPointsMakeNothing()
    {
        var (context, layer, _) = NewContext();

        Assert.False(InkEditing.Commit(context, [], zoom: 1));
        Assert.Empty(layer.Shapes);

        // a single point is a dot (the dot of an i)
        Assert.True(InkEditing.Commit(context, [new SKPoint(5, 5)], zoom: 1));
        Assert.Single(layer.Shapes);
    }

    [Fact]
    public void TheSmoothingFollowsTheZoom()
    {
        var (context, _, _) = NewContext();
        context.PenSmoothing = 8;

        // the hand shakes by screen pixels: zoomed in twice, half the page distance is enough
        Assert.Equal(InkEditing.SmoothingDistance(context, 1) / 2, InkEditing.SmoothingDistance(context, 2), 4);
    }
}
