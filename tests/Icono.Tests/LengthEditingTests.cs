using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Tests;

public class LengthEditingTests
{
    private static (EditorContext Context, Shape Wall, List<string> Messages) RoomAtOneToFifty()
    {
        var context = new EditorContext();
        context.Document.Width = 800;
        context.Document.Height = 800;
        context.Document.ScaleDenominator = 50;
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;

        var wall = WallRooms.Build(RoomPreset.Closed, 400, 400, 400, 300, 50);
        layer.Shapes.Add(wall);

        var messages = new List<string>();
        context.StatusMessage += messages.Add;
        return (context, wall, messages);
    }

    private static double Px(string text, double scale = 50) =>
        WallGeometry.TryParseLength(text, LengthUnit.Centimeter, scale, out var px) ? px : double.NaN;

    [Fact]
    public void TypingALength_MakesTheWallThatLong_AndUndoTakesItBack()
    {
        var (context, wall, messages) = RoomAtOneToFifty();
        var before = WallGeometry.MeasuredLength(wall, 0);

        Assert.True(LengthEditing.CommitWall(context, wall, 0, "250"));

        Assert.Equal(Px("250"), WallGeometry.MeasuredLength(wall, 0), 1);
        Assert.Contains(messages, m => m.StartsWith("Wall set to"));

        context.Undo();
        Assert.Equal(before, WallGeometry.MeasuredLength(wall, 0), 1);
    }

    [Fact]
    public void ATextThatIsNoLength_ChangesNothing_AndSaysSo()
    {
        var (context, wall, messages) = RoomAtOneToFifty();
        var before = WallGeometry.MeasuredLength(wall, 0);

        Assert.False(LengthEditing.CommitWall(context, wall, 0, "abc"));

        Assert.Equal(before, WallGeometry.MeasuredLength(wall, 0), 6);
        Assert.Contains(messages, m => m.StartsWith("That is not a length"));
        Assert.False(context.UndoManager.CanUndo);
    }

    [Fact]
    public void TheCurrentLength_IsWrittenAsTheDimensionWritesIt()
    {
        var (_, wall, _) = RoomAtOneToFifty();

        var text = LengthEditing.WallText(wall, 0);

        // the editor starts with the number the user tapped, in a form it can read back
        Assert.True(WallGeometry.TryParseLength(text, WallRenderer.SegmentUnit(wall, 0), 50, out var px));
        Assert.Equal(WallGeometry.MeasuredLength(wall, 0), px, 0);
    }

    [Fact]
    public void ADoorWidthThatIsNoLength_IsRefused()
    {
        var (context, _, messages) = RoomAtOneToFifty();
        var door = new Shape { Kind = ShapeKind.DoorSingle, Width = 40, Height = 40 };

        Assert.False(LengthEditing.CommitSymbol(context, door, SymbolDimensionPart.Width, "wide"));
        Assert.Contains(messages, m => m.StartsWith("That is not a length"));
    }
}
