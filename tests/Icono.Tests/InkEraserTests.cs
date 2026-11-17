using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Tests;

public class InkEraserTests
{
    private static Shape Stroke(float y)
    {
        // a stroke along y, x from 10 to 90
        var points = Enumerable.Range(0, 40).Select(i => new SKPoint(10 + i * 2, y)).ToList();
        return Icono.Rendering.FreehandBuilder.CreateShape(points, ArgbColor.FromRgb(0, 0, 0), 2, 0.5f)!;
    }

    private static (EditorContext Context, Layer Layer, Shape A, Shape B, Shape Box) Sample()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        var a = Stroke(20);
        var b = Stroke(60);
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 10, Y = 18, Width = 80, Height = 6 }; // a rectangle right on stroke A
        layer.Shapes.Add(a);
        layer.Shapes.Add(box);
        layer.Shapes.Add(b);
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        return (context, layer, a, b, box);
    }

    [Fact]
    public void TheEraser_DeletesTheStrokeItTouches_AndNeverAnythingElse()
    {
        var (context, layer, a, b, box) = Sample();
        var erased = new List<(Layer, Shape, int)>();

        var count = InkEditing.EraseAt(context, 50, 20, zoom: 1, erased); // on stroke A, and on the rectangle too

        Assert.Equal(1, count);
        Assert.DoesNotContain(a, layer.Shapes);
        Assert.Contains(box, layer.Shapes); // a rectangle is no handwriting
        Assert.Contains(b, layer.Shapes);
    }

    [Fact]
    public void ATouchBesideAStroke_ErasesNothing_AndTheReachFollowsTheZoom()
    {
        var (context, layer, _, _, _) = Sample();
        var erased = new List<(Layer, Shape, int)>();

        Assert.Equal(0, InkEditing.EraseAt(context, 50, 40, zoom: 1, erased)); // 20 px from both strokes
        Assert.Equal(2, layer.Shapes.Count(s => s.Freehand));

        // zoomed in ten times the same ten screen pixels are one page pixel: 4 px away is out of reach
        Assert.Equal(0, InkEditing.EraseAt(context, 50, 24, zoom: 10, erased));
        Assert.Equal(1, InkEditing.EraseAt(context, 50, 24, zoom: 1, erased));
    }

    [Fact]
    public void LockedStrokesAndLockedLayers_AreLeftAlone()
    {
        var (context, layer, a, b, _) = Sample();
        a.Locked = true;
        var erased = new List<(Layer, Shape, int)>();

        Assert.Equal(0, InkEditing.EraseAt(context, 50, 20, zoom: 1, erased));

        layer.Locked = true;
        Assert.Equal(0, InkEditing.EraseAt(context, 50, 60, zoom: 1, erased));
        Assert.Contains(b, layer.Shapes);
    }

    [Fact]
    public void ADragThroughTwoStrokes_IsOneUndoStep_AndUndoKeepsTheOrder()
    {
        var (context, layer, a, b, box) = Sample();
        var before = layer.Shapes.ToList();
        var erased = new List<(Layer, Shape, int)>();

        InkEditing.EraseAt(context, 50, 20, zoom: 1, erased);
        InkEditing.EraseAt(context, 50, 60, zoom: 1, erased);
        InkEditing.CommitErase(context, erased);
        Assert.Equal([box], layer.Shapes);

        context.Undo();
        Assert.Equal(before, layer.Shapes);

        context.Redo();
        Assert.Equal([box], layer.Shapes);
    }

    [Fact]
    public void ATouchThatTurnsOutToBeAZoom_PutsTheStrokesBack()
    {
        var (context, layer, _, _, _) = Sample();
        var before = layer.Shapes.ToList();
        var erased = new List<(Layer, Shape, int)>();

        InkEditing.EraseAt(context, 50, 20, zoom: 1, erased);
        InkEditing.EraseAt(context, 50, 60, zoom: 1, erased);
        InkEditing.RestoreErased(context, erased);

        Assert.Equal(before, layer.Shapes);
        Assert.False(context.UndoManager.CanUndo);
    }
}
