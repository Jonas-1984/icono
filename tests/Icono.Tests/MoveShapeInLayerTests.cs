using Icono.Core;
using Icono.Editor;

namespace Icono.Tests;

/// <summary>An object moves forward or back within its layer (index 0 is the front-most), one undo step each.</summary>
public class MoveShapeInLayerTests
{
    private static (EditorContext Context, Layer Layer, Shape A, Shape B, Shape C) Setup()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "L" };
        var (a, b, c) = (new Shape { Kind = ShapeKind.Rectangle }, new Shape { Kind = ShapeKind.Ellipse }, new Shape { Kind = ShapeKind.Triangle });
        layer.Shapes.Add(a);
        layer.Shapes.Add(b);
        layer.Shapes.Add(c);
        context.Document.Layers.Add(layer);
        return (context, layer, a, b, c);
    }

    [Fact]
    public void Forward_MovesTowardsIndexZero_AndBackAway()
    {
        var (context, layer, a, b, _) = Setup();

        Assert.True(context.MoveShapeInLayer(layer, b, -1));
        Assert.Equal([b, a], layer.Shapes.Take(2).ToArray());

        Assert.True(context.MoveShapeInLayer(layer, b, 1));
        Assert.Equal([a, b], layer.Shapes.Take(2).ToArray());
    }

    [Fact]
    public void AtTheEnds_NothingMoves()
    {
        var (context, layer, a, _, c) = Setup();

        Assert.False(context.MoveShapeInLayer(layer, a, -1));
        Assert.False(context.MoveShapeInLayer(layer, c, 1));
    }

    [Fact]
    public void ALockedShapeOrLayer_StaysWhereItIs()
    {
        var (context, layer, _, b, _) = Setup();
        b.Locked = true;
        Assert.False(context.MoveShapeInLayer(layer, b, -1));

        b.Locked = false;
        layer.Locked = true;
        Assert.False(context.MoveShapeInLayer(layer, b, -1));
    }

    [Fact]
    public void AMove_IsOneUndoStep()
    {
        var (context, layer, a, b, c) = Setup();
        context.MoveShapeInLayer(layer, c, -2);

        context.Undo();

        Assert.Equal([a, b, c], layer.Shapes.ToArray());
    }
}
