using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

/// <summary>The text of a shape can be written outside it, next to one of its sides.</summary>
public class TextOutsideTests
{
    private static Shape Labelled(TextPlacement placement)
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Rectangle, X = 200, Y = 200, Width = 100, Height = 60, TextContent = "Kitchen", TextFontSize = 14,
            TextPlacement = placement,
        };
        return shape;
    }

    private static SkiaSharp.SKRect Bounds(Shape shape)
    {
        using var path = ShapeLabel.BuildPath(shape);
        return path!.Bounds;
    }

    [Fact]
    public void Inside_KeepsTheTextInTheShape()
    {
        var bounds = Bounds(Labelled(TextPlacement.Inside));

        var box = ConnectionRouter.GetBox(Labelled(TextPlacement.Inside));
        Assert.True(bounds.Left >= box.Left && bounds.Right <= box.Left + box.Width && bounds.Top >= box.Top && bounds.Bottom <= box.Top + box.Height);
        Assert.Null(ShapeLabel.OutsideBounds(Labelled(TextPlacement.Inside)));
    }

    [Fact]
    public void Below_PutsTheTextUnderTheOutline_CentredOnIt()
    {
        var bounds = Bounds(Labelled(TextPlacement.Below));

        var box = ConnectionRouter.GetBox(Labelled(TextPlacement.Below));
        Assert.True(bounds.Top >= box.Top + box.Height, bounds.ToString());
        Assert.Equal(box.Left + box.Width / 2, (bounds.Left + bounds.Right) / 2, 0);
    }

    [Fact]
    public void Above_PutsTheTextOverTheOutline()
    {
        var bounds = Bounds(Labelled(TextPlacement.Above));

        Assert.True(bounds.Bottom <= ConnectionRouter.GetBox(Labelled(TextPlacement.Above)).Top, bounds.ToString());
    }

    [Fact]
    public void LeftAndRight_PutTheTextBesideTheOutline()
    {
        var box = ConnectionRouter.GetBox(Labelled(TextPlacement.Left));
        Assert.True(Bounds(Labelled(TextPlacement.Left)).Right <= box.Left);
        Assert.True(Bounds(Labelled(TextPlacement.Right)).Left >= box.Left + box.Width);
    }

    [Fact]
    public void OutsideText_IsDarkUnlessAColourIsChosen_AndInsideTextFollowsTheFill()
    {
        var dark = ArgbColor.FromRgb(0x1B, 0x23, 0x32);
        var outside = Labelled(TextPlacement.Below);
        outside.FillColor = ArgbColor.FromRgb(0x10, 0x10, 0x10);
        Assert.Equal(dark, ShapeLabel.EffectiveColor(outside));

        var inside = Labelled(TextPlacement.Inside);
        inside.FillColor = ArgbColor.FromRgb(0x10, 0x10, 0x10);
        Assert.Equal(ArgbColor.FromRgb(0xFF, 0xFF, 0xFF), ShapeLabel.EffectiveColor(inside));

        outside.TextColor = ArgbColor.FromRgb(200, 0, 0);
        Assert.Equal(ArgbColor.FromRgb(200, 0, 0), ShapeLabel.EffectiveColor(outside));
    }

    [Fact]
    public void ThePlacement_IsPartOfTheSnapshot()
    {
        var shape = Labelled(TextPlacement.Right);
        var copy = new Shape { Kind = ShapeKind.Rectangle };
        ShapeSnapshot.Capture(shape).ApplyTo(copy);

        Assert.Equal(TextPlacement.Right, copy.TextPlacement);
    }
}
