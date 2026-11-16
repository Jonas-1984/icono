using Icono.Core;
using Icono.Editor;

namespace Icono.Tests;

public class LayerEditingTests
{
    private static EditorContext NewContext()
    {
        var context = new EditorContext();
        context.Document.Layers.Add(new Layer { Name = "Layer 1" });
        context.SelectedLayer = context.Document.Layers[0];
        return context;
    }

    [Fact]
    public void AddLayer_PutsANewActiveLayerInFront_AndUndoRemovesIt()
    {
        var context = NewContext();

        var added = context.AddLayer();

        Assert.Same(added, context.Document.Layers[0]);
        Assert.Equal("Layer 2", added.Name);
        Assert.Same(added, context.SelectedLayer);
        context.Undo();
        Assert.Single(context.Document.Layers);
    }

    [Fact]
    public void MoveAndRename_AreUndoable()
    {
        var context = NewContext();
        var back = context.Document.Layers[0];
        context.AddLayer();

        Assert.True(context.MoveLayer(back, -1));
        Assert.Same(back, context.Document.Layers[0]);
        Assert.True(context.RenameLayer(back, " Walls "));
        Assert.Equal("Walls", back.Name);

        context.Undo();
        Assert.Equal("Layer 1", back.Name);
        context.Undo();
        Assert.Same(back, context.Document.Layers[1]);
    }

    [Fact]
    public void DeleteLayer_RemovesItWithItsObjects_KeepsTheLastLayer_AndUndoBringsItBack()
    {
        var context = NewContext();
        var first = context.Document.Layers[0];
        first.Shapes.Add(new Shape { Kind = ShapeKind.Rectangle });

        Assert.False(context.DeleteLayer(first)); // the only layer stays

        context.AddLayer();
        Assert.True(context.DeleteLayer(first));
        Assert.Single(context.Document.Layers);

        context.Undo();
        Assert.Equal(2, context.Document.Layers.Count);
        Assert.Single(first.Shapes);
    }

    [Fact]
    public void MoveSelectionToLayer_MovesTheObjects_AsOneUndoStep()
    {
        var context = NewContext();
        var source = context.Document.Layers[0];
        var shape = new Shape { Kind = ShapeKind.Rectangle };
        source.Shapes.Add(shape);
        var target = context.AddLayer();
        context.SelectedShape = shape;

        Assert.True(context.MoveSelectionToLayer(target));
        Assert.Contains(shape, target.Shapes);
        Assert.DoesNotContain(shape, source.Shapes);

        context.Undo();
        Assert.Contains(shape, source.Shapes);
        Assert.Empty(target.Shapes);
    }
}
