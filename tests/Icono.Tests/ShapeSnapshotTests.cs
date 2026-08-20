using Icono.Core;

namespace Icono.Tests;

public class ShapeSnapshotTests
{
    [Fact]
    public void CaptureThenApplyToADifferentShape_ReproducesAllValues()
    {
        var original = new Shape
        {
            Kind = ShapeKind.Rectangle,
            X = 10,
            Y = 20,
            Width = 30,
            Height = 40,
            FillColor = ArgbColor.FromRgb(1, 2, 3),
            BorderColor = ArgbColor.FromRgb(4, 5, 6),
            BorderThickness = 2,
            CornerRadius = 5,
            Opacity = 80,
            BlurRadius = 3,
            FadeAmount = 25,
            ShadowEnabled = true,
            ShadowDistance = 7,
            ShadowStrength = 60,
            Rotation = 45,
            FlipHorizontal = true,
            FlipVertical = false,
            TextContent = "Hello",
            TextFontSize = 32,
            TextBold = true,
            TextItalic = true,
        };

        var snapshot = ShapeSnapshot.Capture(original);

        var target = new Shape { Kind = ShapeKind.Ellipse };
        snapshot.ApplyTo(target);

        Assert.Equal(original.X, target.X);
        Assert.Equal(original.Y, target.Y);
        Assert.Equal(original.Width, target.Width);
        Assert.Equal(original.Height, target.Height);
        Assert.Equal(original.FillColor, target.FillColor);
        Assert.Equal(original.BorderColor, target.BorderColor);
        Assert.Equal(original.BorderThickness, target.BorderThickness);
        Assert.Equal(original.CornerRadius, target.CornerRadius);
        Assert.Equal(original.Opacity, target.Opacity);
        Assert.Equal(original.BlurRadius, target.BlurRadius);
        Assert.Equal(original.FadeAmount, target.FadeAmount);
        Assert.Equal(original.ShadowEnabled, target.ShadowEnabled);
        Assert.Equal(original.ShadowDistance, target.ShadowDistance);
        Assert.Equal(original.ShadowStrength, target.ShadowStrength);
        Assert.Equal(original.Rotation, target.Rotation);
        Assert.Equal(original.FlipHorizontal, target.FlipHorizontal);
        Assert.Equal(original.FlipVertical, target.FlipVertical);
        Assert.Equal(original.TextContent, target.TextContent);
        Assert.Equal(original.TextFontSize, target.TextFontSize);
        Assert.Equal(original.TextBold, target.TextBold);
        Assert.Equal(original.TextItalic, target.TextItalic);
    }

    [Fact]
    public void TwoSnapshotsOfTheSameState_AreEqual()
    {
        var shape = new Shape { Kind = ShapeKind.Star, X = 1, Y = 2, Width = 3, Height = 4 };

        var a = ShapeSnapshot.Capture(shape);
        var b = ShapeSnapshot.Capture(shape);

        Assert.Equal(a, b);
    }

    [Fact]
    public void RotationSetter_WrapsIntoZeroTo360()
    {
        var shape = new Shape { Kind = ShapeKind.Rectangle };

        shape.Rotation = 370;
        Assert.Equal(10, shape.Rotation);

        shape.Rotation = -10;
        Assert.Equal(350, shape.Rotation);
    }
}
