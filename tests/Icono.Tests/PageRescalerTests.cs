using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

/// <summary>The scale belongs to the page: changing it resizes the drawing on the page, so every real-world size and every dimension keeps its value.</summary>
public class PageRescalerTests : IDisposable
{
    /// <summary>The tests change the global drawing scale: it goes back to the default after each one.</summary>
    public void Dispose() => DrawingScale.Denominator = 1;

    private static Document Page(int pages = 1)
    {
        var document = new Document { Width = 1000, Height = 800, PageCount = pages, ScaleDenominator = 40 };
        document.Layers.Add(new Layer { Name = "L" });
        return document;
    }

    private static void ChangeScale(Document document, double to)
    {
        var from = document.ScaleDenominator;
        document.ScaleDenominator = to;
        DrawingScale.Denominator = to;
        PageRescaler.Rescale(document, PageRescaler.Factor(from, to));
    }

    private static Shape Wall()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 300, Y = 250, WallThickness = 16, WallClosed = true, BorderThickness = 2,
            Points = [new CorePoint(0, 0), new CorePoint(420, 0), new CorePoint(420, 300), new CorePoint(0, 300)],
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    [Fact]
    public void TheDimensionsOfAWall_KeepTheirValues_WhenTheScaleChanges()
    {
        var document = Page();
        var wall = Wall();
        document.Layers[0].Shapes.Add(wall);
        DrawingScale.Denominator = 40;
        var before = Enumerable.Range(0, 4).Select(i => WallRenderer.LayoutSegmentDimension(wall, i)!.Text).ToList();

        ChangeScale(document, 50);

        var after = Enumerable.Range(0, 4).Select(i => WallRenderer.LayoutSegmentDimension(wall, i)!.Text).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public void TheDrawing_IsResizedAboutTheMiddleOfThePage_AndTheWallThicknessKeepsItsRealSize()
    {
        var document = Page();
        var wall = Wall();
        document.Layers[0].Shapes.Add(wall);
        var thicknessReal = wall.WallThickness * 40;

        ChangeScale(document, 50); // the page stands for more: everything is 40 / 50 as big on the page

        Assert.Equal(500 + (300 - 500) * 0.8, wall.X, 6);
        Assert.Equal(400 + (250 - 400) * 0.8, wall.Y, 6);
        Assert.Equal(420 * 0.8, wall.Width, 6);
        Assert.Equal(300 * 0.8, wall.Height, 6);
        Assert.Equal(thicknessReal, wall.WallThickness * 50, 6);
    }

    [Fact]
    public void WhatBelongsToThePaper_StaysAsItIs()
    {
        var document = Page();
        var wall = Wall();
        wall.DimOffset = 20;
        wall.DimFontSize = 11;
        wall.HatchSpacing = 8;
        document.Layers[0].Shapes.Add(wall);

        ChangeScale(document, 100);

        Assert.Equal(2, wall.BorderThickness);   // the weight of the line
        Assert.Equal(20, wall.DimOffset);        // the distance of a dimension line
        Assert.Equal(11, wall.DimFontSize);      // the size of its number
        Assert.Equal(8, wall.HatchSpacing);
    }

    [Fact]
    public void ATextKeepsItsSize_ButFollowsTheDrawingOnThePage()
    {
        var document = Page();
        var text = new Shape { Kind = ShapeKind.Text, X = 700, Y = 300, Width = 120, Height = 30, TextFontSize = 14 };
        document.Layers[0].Shapes.Add(text);

        ChangeScale(document, 80); // factor 0.5

        Assert.Equal(120, text.Width);
        Assert.Equal(30, text.Height);
        Assert.Equal(14, text.TextFontSize);
        Assert.Equal(500 + (760 - 500) * 0.5 - 60, text.X, 6); // its middle moved like everything else
        Assert.Equal(400 + (315 - 400) * 0.5 - 15, text.Y, 6);
    }

    [Fact]
    public void AShapeWithAFrame_IsScaledAboutThePageMiddle_WithItsCornerRadius()
    {
        var document = Page();
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 100, CornerRadius = 10 };
        document.Layers[0].Shapes.Add(box);

        ChangeScale(document, 80); // 0.5

        Assert.Equal(100, box.Width, 6);
        Assert.Equal(50, box.Height, 6);
        Assert.Equal(500 + (200 - 500) * 0.5 - 50, box.X, 6);   // the middle (200, 150) -> (350, 275)
        Assert.Equal(400 + (150 - 400) * 0.5 - 25, box.Y, 6);
        Assert.Equal(5, box.CornerRadius, 6);
    }

    [Fact]
    public void HiddenAndLockedLayers_AreResizedToo_SoNothingIsLeftBehind()
    {
        var document = Page();
        var hidden = new Layer { Name = "Hidden", Visible = false };
        var locked = new Layer { Name = "Locked", Locked = true };
        var a = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 100, Height = 100 };
        var b = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 100, Height = 100 };
        hidden.Shapes.Add(a);
        locked.Shapes.Add(b);
        document.Layers.Add(hidden);
        document.Layers.Add(locked);

        ChangeScale(document, 80);

        Assert.Equal(50, a.Width, 6);
        Assert.Equal(50, b.Width, 6);
    }

    [Fact]
    public void OnASecondPage_TheDrawingIsScaledAboutTheMiddleOfThatPage()
    {
        var document = Page(2);
        var second = document.PageTop(1);
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 200, Y = second + 100, Width = 100, Height = 100 };
        document.Layers[0].Shapes.Add(box);

        ChangeScale(document, 80); // 0.5

        // about the middle of the second page (500, second + 400), not of the first one
        Assert.Equal(500 + (250 - 500) * 0.5 - 25, box.X, 6);
        Assert.Equal(second + 400 + (150 - 400) * 0.5 - 25, box.Y, 6);
    }

    [Fact]
    public void ChangingTheScaleThereAndBack_GivesTheDrawingBack()
    {
        var document = Page();
        var wall = Wall();
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 120, Y = 90, Width = 210, Height = 70, CornerRadius = 6 };
        document.Layers[0].Shapes.Add(wall);
        document.Layers[0].Shapes.Add(box);

        ChangeScale(document, 125);
        ChangeScale(document, 40);

        Assert.Equal(300, wall.X, 6);
        Assert.Equal(250, wall.Y, 6);
        Assert.Equal(420, wall.Width, 6);
        Assert.Equal(16, wall.WallThickness, 6);
        Assert.Equal(new[] { 0d, 420d, 420d, 0d }, wall.Points!.Select(p => Math.Round(p.X, 6)).ToArray());
        Assert.Equal(120, box.X, 6);
        Assert.Equal(210, box.Width, 6);
        Assert.Equal(6, box.CornerRadius, 6);
    }

    [Fact]
    public void AFreeDimension_KeepsItsValue_WhenItsPointsAreResized()
    {
        var document = Page();
        var dimension = new Shape
        {
            Kind = ShapeKind.Dimension, DimKind = DimensionKind.Aligned, X = 200, Y = 200, DimOffset = 28,
            Points = [new CorePoint(0, 0), new CorePoint(300, 0), new CorePoint(0, 28)],
        };
        PathBoundsCalculator.Recompute(dimension);
        document.Layers[0].Shapes.Add(dimension);
        DrawingScale.Denominator = 40;
        var before = DimensionRenderer.Layout(dimension).Text;

        ChangeScale(document, 50);

        Assert.Equal(before, DimensionRenderer.Layout(dimension).Text);
    }

    [Fact]
    public void ADesignGrid_IsNotAtAScale_AndStaysWhereItIs()
    {
        var document = Page();
        var grid = new Shape { Kind = ShapeKind.DesignGrid, X = 0, Y = 0, Width = 1000, Height = 800 };
        document.Layers[0].Shapes.Add(grid);

        ChangeScale(document, 80);

        Assert.Equal(1000, grid.Width);
        Assert.Equal(800, grid.Height);
    }
}
