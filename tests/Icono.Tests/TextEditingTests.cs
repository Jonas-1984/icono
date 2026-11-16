using Icono.Core;
using Icono.Editor;

namespace Icono.Tests;

public class TextEditingTests
{
    private static (EditorContext Context, Layer Layer) NewContext()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        return (context, layer);
    }

    [Fact]
    public void ANewText_IsPlacedSelectedAndUndoable()
    {
        var (context, layer) = NewContext();
        context.ActiveTool = ActiveTool.Shape;

        TextEditing.CommitTextObject(context, null, layer, 40, 50, "Kitchen");

        var text = Assert.Single(layer.Shapes);
        Assert.Equal(ShapeKind.Text, text.Kind);
        Assert.Equal("Kitchen", text.TextContent);
        Assert.Equal((40.0, 50.0), (text.X, text.Y));
        Assert.Same(text, context.SelectedShape);
        Assert.Equal(ActiveTool.Select, context.ActiveTool);

        context.Undo();
        Assert.Empty(layer.Shapes);
    }

    [Fact]
    public void EmptyingAText_RemovesIt()
    {
        var (context, layer) = NewContext();
        TextEditing.CommitTextObject(context, null, layer, 0, 0, "Bath");
        var text = layer.Shapes[0];

        TextEditing.CommitTextObject(context, text, layer, 0, 0, "  ");

        Assert.Empty(layer.Shapes);
        context.Undo();
        Assert.Same(text, Assert.Single(layer.Shapes));
    }

    [Fact]
    public void TheFirstLabelOfAShape_GetsAFittingSize_AndAnEmptyLabelIsRemoved()
    {
        var (context, layer) = NewContext();
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 200, Height = 100 };
        layer.Shapes.Add(box);

        TextEditing.CommitLabel(context, box, "Office  \n");
        Assert.Equal("Office", box.TextContent);
        Assert.True(box.TextFontSize > 0);

        TextEditing.CommitLabel(context, box, "");
        Assert.Null(box.TextContent);
        context.Undo();
        Assert.Equal("Office", box.TextContent);
    }
}
