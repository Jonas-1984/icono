using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class WallJoinTests
{
    private static Shape MakeWall(params (double X, double Y)[] points)
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = points[0].X, Y = points[0].Y, WallThickness = 20, Hatch = HatchKind.Solid,
            WallShowDimensions = false, FillColor = ArgbColor.FromRgb(255, 255, 255), BorderColor = ArgbColor.FromRgb(0, 0, 0), BorderThickness = 2,
            Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList(),
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static SKBitmap Render(params Shape[] walls)
    {
        var document = new Document { Width = 200, Height = 200 };
        var layer = new Layer { Name = "L" };
        foreach (var wall in walls)
        {
            layer.Shapes.Add(wall);
        }

        document.Layers.Add(layer);
        var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        DocumentRenderer.Render(canvas, document, 200, 200, forExport: true);
        return bitmap;
    }

    [Fact]
    public void TwoWallsThatMeetInACorner_AreMitredWithNoNotchOnTheOutside()
    {
        // vertical wall down from (50,50) and horizontal wall to the right from (50,50): the outer corner is at (40,40)
        using var apart = Render(MakeWall((50, 50), (50, 150)), MakeWall((50, 50), (150, 50)));

        Assert.NotEqual(0, apart.GetPixel(42, 42).Alpha);
        Assert.Equal(0, apart.GetPixel(30, 30).Alpha);
    }

    [Fact]
    public void NoBorderLineRunsThroughTheJoint()
    {
        using var bitmap = Render(MakeWall((50, 50), (50, 150)), MakeWall((50, 50), (150, 50)));

        // inside the joint (where the end line of the first band used to be drawn) the wall is plain white
        var inside = bitmap.GetPixel(55, 50);
        Assert.True(inside.Red > 200 && inside.Green > 200 && inside.Blue > 200, $"seam in the joint: {inside}");
        // the outer border is still there
        var border = bitmap.GetPixel(100, 40);
        Assert.True(border.Red < 80, $"border missing: {border}");
    }

    [Fact]
    public void AWallEndingOnTheSideOfAnother_HasNoSeamEither()
    {
        // a horizontal wall through (50..150, 100) and a vertical one ending on its centre line
        using var bitmap = Render(MakeWall((50, 100), (150, 100)), MakeWall((100, 20), (100, 100)));

        var inside = bitmap.GetPixel(100, 100);
        Assert.True(inside.Red > 200, $"seam in the T-joint: {inside}");
    }

    [Fact]
    public void WallsThatDoNotTouch_StayAsTheyWere()
    {
        using var bitmap = Render(MakeWall((20, 50), (80, 50)), MakeWall((120, 50), (180, 50)));

        Assert.NotEqual(0, bitmap.GetPixel(50, 50).Alpha);
        Assert.Equal(0, bitmap.GetPixel(100, 50).Alpha);
    }

    [Fact]
    public void WallsWithDifferentBorders_AreNotMerged()
    {
        var a = MakeWall((50, 50), (50, 150));
        var b = MakeWall((50, 50), (150, 50));
        b.BorderColor = ArgbColor.FromRgb(200, 0, 0);

        using var bitmap = Render(a, b);

        Assert.Equal(0, bitmap.GetPixel(42, 42).Alpha); // still the butt-ended bands
    }

    [Fact]
    public void TheJoinsAreOnlyActiveWhileTheDocumentIsRendered()
    {
        var a = MakeWall((50, 50), (50, 150));
        var b = MakeWall((50, 50), (150, 50));
        using (Render(a, b))
        {
        }

        Assert.Null(WallJoiner.For(a));
        Assert.Null(WallJoiner.For(b));
    }
}
