using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class HandwritingToTextTests
{
    private static readonly SKPoint[] First = [new(10, 10), new(20, 30), new(30, 50), new(40, 70)];
    private static readonly SKPoint[] Second = [new(60, 10), new(60, 40), new(60, 70)];

    private static (EditorContext Context, Layer Layer) NewContext()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        return (context, layer);
    }

    [Fact]
    public void TheStrokesJustWritten_AreOffered_OldestFirst()
    {
        var (context, _) = NewContext();
        InkEditing.Commit(context, First, 1);
        InkEditing.Commit(context, Second, 1);

        var strokes = HandwritingToText.Strokes(context);

        Assert.Equal(2, strokes.Count);
        Assert.True(strokes[0].X < strokes[1].X); // the first stroke was written first, it lies on the left
    }

    [Fact]
    public void Sampling_GivesOnePointListPerStroke_AlongTheStroke()
    {
        var (context, _) = NewContext();
        InkEditing.Commit(context, First, 1);
        InkEditing.Commit(context, Second, 1);

        var sampled = HandwritingToText.Sample(HandwritingToText.Strokes(context));

        Assert.Equal(2, sampled.Count);
        Assert.All(sampled, s => Assert.True(s.Count > 5));
        Assert.True(sampled[1].All(p => Math.Abs(p.X - 60) < 3));
    }

    [Fact]
    public void Replace_PutsATextWhereTheWritingWas_InThePenColour_AsOneUndoStep()
    {
        var (context, layer) = NewContext();
        context.PenColor = ArgbColor.FromRgb(20, 80, 200);
        InkEditing.Commit(context, First, 1);
        InkEditing.Commit(context, Second, 1);
        var strokes = HandwritingToText.Strokes(context);
        var left = strokes.Min(s => s.X);
        var right = strokes.Max(s => s.X + s.Width);

        Assert.True(HandwritingToText.Replace(context, strokes, "Hallo"));

        var text = Assert.Single(layer.Shapes);
        Assert.Equal(ShapeKind.Text, text.Kind);
        Assert.Equal("Hallo", text.TextContent);
        Assert.Equal(ArgbColor.FromRgb(20, 80, 200), text.FillColor);
        Assert.InRange(text.X + text.Width / 2, (left + right) / 2 - 2, (left + right) / 2 + 2);
        Assert.Empty(context.RecentInk);

        context.Undo();
        Assert.Equal(2, layer.Shapes.Count);
        Assert.All(layer.Shapes, s => Assert.True(s.Freehand));

        context.Redo();
        Assert.Equal(ShapeKind.Text, Assert.Single(layer.Shapes).Kind);
    }

    [Fact]
    public void WithoutAText_OrWithoutStrokes_NothingChanges()
    {
        var (context, layer) = NewContext();
        InkEditing.Commit(context, First, 1);
        var strokes = HandwritingToText.Strokes(context);

        Assert.False(HandwritingToText.Replace(context, strokes, "  "));
        Assert.False(HandwritingToText.Replace(context, [], "Hi"));
        Assert.Single(layer.Shapes);
        Assert.True(Assert.Single(layer.Shapes).Freehand);
    }

    [Fact]
    public void ASelectedHandwriting_IsConverted_EvenWhenNotJustWritten()
    {
        var (context, layer) = NewContext();
        InkEditing.Commit(context, First, 1);
        context.ForgetInk();
        Assert.Empty(HandwritingToText.Strokes(context));

        context.SelectedShapes.Add(layer.Shapes[0]);

        Assert.Single(HandwritingToText.Strokes(context));
    }

    [Fact]
    public void TheFontSize_FollowsTheWidthOfTheWriting_ButNotItsHeight()
    {
        var wide = HandwritingToText.FitFontSize("Wort", 400, 300);
        var flat = HandwritingToText.FitFontSize("Wort", 400, 20);

        Assert.True(flat < wide);
        Assert.True(flat <= 20 * 1.15 + 1e-6);
    }
}
