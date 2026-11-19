using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>Handwriting to text takes ONE piece of writing (strokes that lie together), not everything drawn with the pen since the last conversion.</summary>
[Collection("Scale")]
public class InkGroupTests
{
    // a "word": three strokes of letter height 60 close to each other
    private static readonly SKPoint[][] Word =
    [
        [new(10, 10), new(10, 40), new(10, 70)],
        [new(40, 10), new(40, 40), new(40, 70)],
        [new(70, 10), new(70, 40), new(70, 70)],
    ];

    // a drawing far to the right: two long strokes
    private static readonly SKPoint[][] Drawing =
    [
        [new(600, 300), new(700, 400), new(800, 500)],
        [new(800, 300), new(700, 400), new(600, 500)],
    ];

    private static EditorContext NewContext()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        return context;
    }

    private static void Write(EditorContext context, params SKPoint[][] strokes)
    {
        foreach (var stroke in strokes)
        {
            InkEditing.Commit(context, stroke, 1);
        }
    }

    [Fact]
    public void SeparateDrawings_AreSeparateGroups()
    {
        var context = NewContext();
        Write(context, Word);
        Write(context, Drawing);

        var groups = HandwritingToText.RecentGroups(context);

        Assert.Equal(2, groups.Count);
        Assert.Equal(3, groups[0].Count); // the word, written first
        Assert.Equal(2, groups[1].Count); // the drawing, written last
    }

    [Fact]
    public void WithoutASelection_OnlyTheGroupWrittenLastIsOffered()
    {
        var context = NewContext();
        Write(context, Word);
        Write(context, Drawing);

        var offered = HandwritingToText.Strokes(context);

        Assert.Equal(2, offered.Count);
        Assert.All(offered, s => Assert.True(s.X > 500)); // the drawing, not the word

        context.SetInkGroup(1); // another group is picked
        var other = HandwritingToText.Strokes(context);
        Assert.Equal(3, other.Count);
        Assert.All(other, s => Assert.True(s.X < 200));
    }

    [Fact]
    public void ANewStroke_OffersTheLastGroupAgain()
    {
        var context = NewContext();
        Write(context, Word);
        Write(context, Drawing);
        context.SetInkGroup(1);

        Write(context, [new SKPoint(300, 600), new SKPoint(310, 650)]);

        Assert.Equal(0, context.InkGroupIndex);
        Assert.Single(HandwritingToText.Strokes(context)); // the new stroke lies alone
    }

    [Fact]
    public void ALateStrokeAddedToAnEarlierWord_MakesThatWordTheLastGroup()
    {
        var context = NewContext();
        Write(context, Word[0], Word[1]);
        Write(context, Drawing);
        Write(context, Word[2]); // the third letter of the word, written after the drawing

        var offered = HandwritingToText.Strokes(context);

        Assert.Equal(3, offered.Count);
        Assert.All(offered, s => Assert.True(s.X < 200));
        Assert.True(offered[0].X < offered[1].X && offered[1].X < offered[2].X, "in the order of writing");
    }

    [Fact]
    public void TheDotOfAnI_BelongsToItsStem()
    {
        var context = NewContext();
        Write(context, [new SKPoint(10, 30), new SKPoint(10, 70)]); // the stem
        Write(context, [new SKPoint(10, 12), new SKPoint(11, 13)]); // the dot, a little above

        Assert.Single(HandwritingToText.RecentGroups(context));
    }

    [Fact]
    public void ASelection_IsConvertedAsChosen_EvenWhenItIsSeveralGroups()
    {
        var context = NewContext();
        Write(context, Word);
        Write(context, Drawing);
        context.SelectedShapes.Clear();
        foreach (var shape in context.Document.Layers[0].Shapes)
        {
            context.SelectedShapes.Add(shape);
        }

        var strokes = HandwritingToText.Strokes(context);

        Assert.True(HandwritingToText.IsSelection(context));
        Assert.Equal(5, strokes.Count);
        Assert.Equal(2, HandwritingToText.Groups(strokes).Count);
    }

    [Fact]
    public void AfterOneGroupBecameText_TheOtherWritingStaysOnOffer()
    {
        var context = NewContext();
        Write(context, Word);
        Write(context, Drawing);
        var word = HandwritingToText.RecentGroups(context)[0];

        Assert.True(HandwritingToText.Replace(context, word, "Hallo"));

        Assert.Equal(2, context.RecentInk.Count); // the two strokes of the drawing are still there
        Assert.Equal(1, context.Document.Layers[0].Shapes.Count(s => s.Kind == ShapeKind.Text));
        Assert.Equal(2, context.Document.Layers[0].Shapes.Count(s => s.Freehand));
        Assert.Equal(2, HandwritingToText.Strokes(context).Count);
    }
}
