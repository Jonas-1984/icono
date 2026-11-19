using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class DimensionColourTests
{
    private static Shape Wall()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 60, Y = 100, WallThickness = 16, Hatch = HatchKind.Solid, BorderThickness = 2, WallShowDimensions = true,
            FillColor = ArgbColor.FromRgb(90, 90, 90), BorderColor = ArgbColor.FromRgb(40, 40, 40),
            Points = [new CorePoint(0, 0), new CorePoint(200, 0), new CorePoint(200, 140)],
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    /// <summary>How many pixels look red (the usual colour of dimensions) and how many look green.</summary>
    private static (int Red, int Green) Count(Shape wall)
    {
        var document = new Document { Width = 400, Height = 320 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);
        using var bitmap = new SKBitmap(400, 320);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        DocumentRenderer.Render(canvas, document, 400, 320, forExport: true);
        var red = 0;
        var green = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red > 150 && p.Green < 110 && p.Blue < 110)
                {
                    red++;
                }
                else if (p.Green > 140 && p.Red < 110 && p.Blue < 140)
                {
                    green++;
                }
            }
        }

        return (red, green);
    }

    [Fact]
    public void TheUsualDimensionsAreRed()
    {
        var (red, green) = Count(Wall());

        Assert.True(red > 50, $"red {red}");
        Assert.Equal(0, green);
    }

    [Fact]
    public void AWallDimensionColour_ColoursEveryDimensionOfTheWall()
    {
        var wall = Wall();
        wall.DimColor = ArgbColor.FromRgb(20, 190, 40);

        var (red, green) = Count(wall);

        Assert.True(green > 50, $"green {green}");
        Assert.True(red < 5, $"red {red}");
    }

    [Fact]
    public void ASegmentColour_ColoursOnlyThatDimension()
    {
        var both = Wall();
        both.DimColor = ArgbColor.FromRgb(20, 190, 40);
        var oneOnly = Wall();
        oneOnly.WallDimensionOverrides = [new WallDimensionOverride(0, Color: ArgbColor.FromRgb(20, 190, 40))];

        var (redAll, greenAll) = Count(both);
        var (redOne, greenOne) = Count(oneOnly);

        Assert.True(greenOne > 20 && greenOne < greenAll, $"one {greenOne} of {greenAll}");
        Assert.True(redOne > redAll, $"the other dimension stays red: {redOne}");
    }

    [Fact]
    public void ADimensionColour_IsPartOfUndo_AndAnOwnLook()
    {
        var wall = Wall();
        wall.DimColor = ArgbColor.FromRgb(1, 2, 3);
        var copy = new Shape { Kind = ShapeKind.Wall };
        ShapeSnapshot.Capture(wall).ApplyTo(copy);
        Assert.Equal(ArgbColor.FromRgb(1, 2, 3), copy.DimColor);

        var own = new WallDimensionOverride(0, Color: ArgbColor.FromRgb(9, 9, 9));
        Assert.True(own.HasOwnStyle);
        Assert.False(own.IsDefault);
        Assert.Null(own.WithoutStyle().Color);
    }
}
