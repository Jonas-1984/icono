using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class ShapeOutlineTests
{
    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.RoundedRectangle)]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.Triangle)]
    public void SetBox_MakesTheDrawnShapeExactlyThatLarge(ShapeKind kind)
    {
        var shape = new Shape { Kind = kind, X = 50, Y = 60, Width = 80, Height = 80 };
        const double tenCm = 10 / 2.54 * 96;

        ShapeOutline.SetBox(shape, 100, 120, tenCm, tenCm);

        var box = ShapeOutline.Box(shape);
        Assert.Equal(tenCm, box.W, 1);
        Assert.Equal(tenCm, box.H, 1);
        Assert.Equal(100, box.X, 1);
        Assert.Equal(120, box.Y, 1);
    }

    [Fact]
    public void Lines_AndTextUseTheirFrame()
    {
        var text = new Shape { Kind = ShapeKind.Text, X = 5, Y = 6, Width = 70, Height = 20 };

        Assert.Equal((5.0, 6.0, 70.0, 20.0), ShapeOutline.Box(text));
    }
}
