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
}
