using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class ShapeLabelTests
{
    [Fact]
    public void OrdinaryShapes_CanCarryText_LinesAndTextShapesCannot()
    {
        Assert.True(ShapeLabel.CanHaveLabel(new Shape { Kind = ShapeKind.Rectangle }));
        Assert.True(ShapeLabel.CanHaveLabel(new Shape { Kind = ShapeKind.Ellipse }));
        Assert.False(ShapeLabel.CanHaveLabel(new Shape { Kind = ShapeKind.Text }));
        Assert.False(ShapeLabel.CanHaveLabel(new Shape { Kind = ShapeKind.Arrow, Points = [new CorePoint(0, 0), new CorePoint(5, 5)] }));
    }

    [Fact]
    public void Label_IsCentredInsideTheShapeAndWrapsToItsWidth()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 120, Height = 80,
            TextContent = "A fairly long sentence that has to wrap", TextFontSize = 14,
        };

        using var path = ShapeLabel.BuildPath(shape);

        Assert.NotNull(path);
        var box = ConnectionRouter.GetBox(shape);
        Assert.True(path!.Bounds.Width <= box.Width, "wrapped text must not be wider than the shape");
        Assert.InRange(path.Bounds.MidX, box.Left + box.Width / 2 - 2, box.Left + box.Width / 2 + 2);
        Assert.InRange(path.Bounds.MidY, box.Top + box.Height / 2 - 10, box.Top + box.Height / 2 + 10);
    }

    [Fact]
    public void AutomaticColour_ReadsOnLightAndDarkFills()
    {
        var onLight = new Shape { Kind = ShapeKind.Rectangle, FillColor = ArgbColor.FromRgb(250, 250, 250) };
        var onDark = new Shape { Kind = ShapeKind.Rectangle, FillColor = ArgbColor.FromRgb(10, 10, 30) };
        var chosen = new Shape { Kind = ShapeKind.Rectangle, TextColor = ArgbColor.FromRgb(200, 0, 0) };

        Assert.True(ShapeLabel.EffectiveColor(onLight).R < 100);
        Assert.Equal(255, ShapeLabel.EffectiveColor(onDark).R);
        Assert.Equal(200, ShapeLabel.EffectiveColor(chosen).R);
    }

    [Fact]
    public void EmptyLabel_BuildsNothing()
    {
        Assert.Null(ShapeLabel.BuildPath(new Shape { Kind = ShapeKind.Rectangle, TextContent = "" }));
    }

    private static Shape Labelled(TextHorizontalAlign h, TextVerticalAlign v, string text = "Hello") => new()
    {
        Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 120,
        TextContent = text, TextFontSize = 14, TextHAlign = h, TextVAlign = v,
    };

    [Theory]
    [InlineData(TextHorizontalAlign.Left, TextVerticalAlign.Top)]
    [InlineData(TextHorizontalAlign.Right, TextVerticalAlign.Bottom)]
    [InlineData(TextHorizontalAlign.Center, TextVerticalAlign.Top)]
    [InlineData(TextHorizontalAlign.Left, TextVerticalAlign.Middle)]
    [InlineData(TextHorizontalAlign.Right, TextVerticalAlign.Middle)]
    [InlineData(TextHorizontalAlign.Center, TextVerticalAlign.Bottom)]
    public void TheNinePositions_PutTheTextAgainstTheChosenEdges(TextHorizontalAlign h, TextVerticalAlign v)
    {
        var shape = Labelled(h, v);
        var box = ConnectionRouter.GetBox(shape);
        float l = (float)box.Left, t = (float)box.Top, r = (float)(box.Left + box.Width), bt = (float)(box.Top + box.Height);
        using var path = ShapeLabel.BuildPath(shape);

        Assert.NotNull(path);
        var b = path!.Bounds;
        switch (h)
        {
            case TextHorizontalAlign.Left: Assert.InRange(b.Left, l + 5, l + 10); break;
            case TextHorizontalAlign.Right: Assert.InRange(b.Right, r - 10, r - 5); break;
            default: Assert.InRange(b.MidX, (l + r) / 2 - 1, (l + r) / 2 + 1); break;
        }

        switch (v)
        {
            case TextVerticalAlign.Top: Assert.InRange(b.Top, t, t + 16); break;
            case TextVerticalAlign.Bottom: Assert.InRange(b.Bottom, bt - 16, bt); break;
            default: Assert.InRange(b.MidY, (t + bt) / 2 - 6, (t + bt) / 2 + 6); break;
        }
    }

    [Fact]
    public void Justify_SpreadsAllLinesButTheLastOfAParagraphAcrossTheFullWidth()
    {
        var text = "one two three four five six seven eight nine ten eleven twelve";
        var left = Labelled(TextHorizontalAlign.Left, TextVerticalAlign.Top, text);
        var justified = Labelled(TextHorizontalAlign.Justify, TextVerticalAlign.Top, text);
        justified.Width = left.Width = 120;

        using var leftPath = ShapeLabel.BuildPath(left);
        using var justifiedPath = ShapeLabel.BuildPath(justified);

        Assert.True(justifiedPath!.Bounds.Width >= leftPath!.Bounds.Width);
        var box = ConnectionRouter.GetBox(justified);
        Assert.InRange(justifiedPath.Bounds.Left, box.Left + 5, box.Left + 10);
        Assert.InRange(justifiedPath.Bounds.Right, box.Left + box.Width - 7, box.Left + box.Width - 5);
    }

    [Fact]
    public void TheAlignmentSurvivesAnUndoSnapshot()
    {
        var shape = Labelled(TextHorizontalAlign.Right, TextVerticalAlign.Bottom);
        var snapshot = ShapeSnapshot.Capture(shape);

        shape.TextHAlign = TextHorizontalAlign.Left;
        shape.TextVAlign = TextVerticalAlign.Top;
        snapshot.ApplyTo(shape);

        Assert.Equal(TextHorizontalAlign.Right, shape.TextHAlign);
        Assert.Equal(TextVerticalAlign.Bottom, shape.TextVAlign);
    }
}
