using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class DimensionTests
{
    private static (Document Document, Shape Box) Build()
    {
        var document = new Document { Width = 600, Height = 400 };
        var layer = new Layer { Name = "L" };
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 120 };
        layer.Shapes.Add(box);
        document.Layers.Add(layer);
        return (document, box);
    }

    private static Shape Free(DimensionKind kind, params (double X, double Y)[] points)
    {
        var dim = new Shape { Kind = ShapeKind.Dimension, DimKind = kind, X = points[0].X, Y = points[0].Y };
        DimensionBuilder.ApplyStyle(dim);
        dim.Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList();
        PathBoundsCalculator.Recompute(dim);
        return dim;
    }

    [Fact]
    public void Values_AreWrittenInTheChosenUnitAndPrecision()
    {
        Assert.Equal("2.54 cm", DimensionRenderer.Format(96, LengthUnit.Centimeter, 2));
        Assert.Equal("25 mm", DimensionRenderer.Format(96, LengthUnit.Millimeter, 0));
        Assert.Equal("1.000 in", DimensionRenderer.Format(96, LengthUnit.Inch, 3));
    }

    [Fact]
    public void HorizontalVerticalAndAligned_MeasureTheRightDistance()
    {
        var horizontal = DimensionRenderer.Layout(Free(DimensionKind.Horizontal, (0, 0), (96, 50), (0, 80)));
        var vertical = DimensionRenderer.Layout(Free(DimensionKind.Vertical, (0, 0), (96, 96), (-30, 0)));
        var aligned = DimensionRenderer.Layout(Free(DimensionKind.Aligned, (0, 0), (30, 40), (0, 0)));

        Assert.Equal("2.54 cm", horizontal.Text);
        Assert.Equal("2.54 cm", vertical.Text);
        Assert.Equal(DimensionRenderer.Format(50, LengthUnit.Centimeter, 2), aligned.Text);
        Assert.Equal(2, horizontal.Arrows.Count);
    }

    [Fact]
    public void Angular_MeasuresTheAngleBetweenTwoLines()
    {
        var layout = DimensionRenderer.Layout(Free(DimensionKind.Angular, (0, 0), (100, 0), (0, 100), (40, 40)));

        Assert.Equal("90.00°", layout.Text);
    }

    [Fact]
    public void RadiusAndDiameter_MeasureACircle()
    {
        var radius = DimensionRenderer.Layout(Free(DimensionKind.Radius, (0, 0), (48, 0)));
        var diameter = DimensionRenderer.Layout(Free(DimensionKind.Diameter, (0, 0), (48, 0)));

        Assert.StartsWith("R 1.27", radius.Text);
        Assert.StartsWith("Ø 2.54", diameter.Text);
    }

    [Fact]
    public void YourOwnText_CanKeepTheMeasuredValue()
    {
        var dim = Free(DimensionKind.Horizontal, (0, 0), (96, 0), (0, 20));
        dim.DimText = "Door: <>";

        Assert.Equal("Door: 2.54 cm", DimensionRenderer.Layout(dim).Text);
    }

    [Fact]
    public void AutoDimensions_OfARectangle_AreWidthAndHeight()
    {
        var (_, box) = Build();

        var dims = DimensionBuilder.Create(box, DimensionSet.Auto);

        Assert.Equal(2, dims.Count);
        Assert.Contains(dims, d => d.DimRole == DimensionRole.Width);
        Assert.Contains(dims, d => d.DimRole == DimensionRole.Height);
        Assert.All(dims, d => Assert.Equal(box.Id, d.DimTarget));
        var outline = ShapeOutline.Box(box);
        var width = dims.First(d => d.DimRole == DimensionRole.Width);
        Assert.Equal(DimensionRenderer.Format(outline.W, LengthUnit.Centimeter, 2), DimensionRenderer.Layout(width).Text);
    }

    [Fact]
    public void AutoDimensions_OfACircle_AreDiameterAndRadius()
    {
        var circle = new Shape { Kind = ShapeKind.Ellipse, X = 0, Y = 0, Width = 100, Height = 100 };
        ShapeOutline.SetBox(circle, 0, 0, 100, 100); // the drawn outline is a true circle

        var dims = DimensionBuilder.Create(circle, DimensionSet.Auto);

        Assert.Equal([DimensionRole.Diameter, DimensionRole.Radius], dims.Select(d => d.DimRole).ToArray());
    }

    [Fact]
    public void AutoDimensions_OfAPolyline_AreSegmentsAndCorners()
    {
        var line = new Shape
        {
            Kind = ShapeKind.Polyline, X = 0, Y = 0, Width = 100, Height = 100,
            Points = [new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 100)],
        };

        var dims = DimensionBuilder.Create(line, DimensionSet.Auto);

        Assert.Equal(2, dims.Count(d => d.DimRole == DimensionRole.Segment));
        var corner = Assert.Single(dims, d => d.DimRole == DimensionRole.Corner);
        Assert.Equal("90.00°", DimensionRenderer.Layout(corner).Text);
    }

    [Fact]
    public void AttachedDimension_FollowsItsShape()
    {
        var (document, box) = Build();
        var width = DimensionBuilder.Create(box, DimensionSet.Size).First(d => d.DimRole == DimensionRole.Width);
        document.Layers[0].Shapes.Add(width);
        DimensionRouter.Refresh(document);
        var before = DimensionRenderer.Layout(width).Text;

        box.X += 60;
        box.Width += 40;
        DimensionRouter.Refresh(document);

        Assert.NotEqual(before, DimensionRenderer.Layout(width).Text);
        Assert.Equal(ShapeOutline.Box(box).X, width.X, 3);
        Assert.Equal(box.Id, width.DimTarget);
    }

    [Fact]
    public void AttachedDimension_IsFreedWhenMovedByHandOrWhenItsShapeIsDeleted()
    {
        var (document, box) = Build();
        var dims = DimensionBuilder.Create(box, DimensionSet.Size);
        var width = dims[0];
        var height = dims[1];
        document.Layers[0].Shapes.Add(width);
        document.Layers[0].Shapes.Add(height);
        DimensionRouter.Refresh(document);

        width.X += 15; // the user drags the dimension
        document.Layers[0].Shapes.Remove(box);
        DimensionRouter.Refresh(document);

        Assert.Null(width.DimTarget);
        Assert.Null(height.DimTarget);
    }

    [Fact]
    public void DimensionsRenderAndAreHit()
    {
        var dim = Free(DimensionKind.Aligned, (20, 50), (120, 50), (20, 80));

        using var bitmap = new SKBitmap(200, 150);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        ShapeRenderer.Draw(canvas, dim);

        Assert.NotEqual(SKColors.White, bitmap.GetPixel(70, 80));
        Assert.True(DimensionRenderer.HitTest(dim, 60, 80, 3));
        Assert.False(DimensionRenderer.HitTest(dim, 60, 130, 3));
    }
}

public class HandPlacedDimensionTests
{
    [Fact]
    public void ADimensionPlacedOnTwoCorners_FollowsMovingAndResizing_AndKeepsItsDistance()
    {
        var document = new Document { Width = 600, Height = 400 };
        var layer = new Layer { Name = "L" };
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 120 };
        layer.Shapes.Add(box);
        document.Layers.Add(layer);

        var topLeft = ConnectionRouter.GetPoint(box, 0);
        var topRight = ConnectionRouter.GetPoint(box, 4);
        var dim = new Shape { Kind = ShapeKind.Dimension, DimKind = DimensionKind.Horizontal, X = topLeft.X, Y = topLeft.Y };
        DimensionBuilder.ApplyStyle(dim);
        dim.Points = [new CorePoint(0, 0), new CorePoint(topRight.X - topLeft.X, topRight.Y - topLeft.Y), new CorePoint(0, -30)];
        PathBoundsCalculator.Recompute(dim);
        layer.Shapes.Add(dim);

        DimensionRouter.AttachPoints(document, dim);
        Assert.NotNull(dim.DimAnchors);
        Assert.NotNull(dim.DimAnchors![0]);
        Assert.NotNull(dim.DimAnchors[1]);
        DimensionRouter.Refresh(document);
        var textBefore = DimensionRenderer.Layout(dim).Text;

        box.X += 50;
        box.Y += 20;
        DimensionRouter.Refresh(document);
        var movedLeft = ShapeOutline.Box(box).X;
        var movedTop = ShapeOutline.Box(box).Y;
        Assert.Equal(movedLeft, dim.Points![0].X + dim.X, 3);
        Assert.Equal(movedTop, dim.Points[0].Y + dim.Y, 3);
        Assert.Equal(movedTop - 30, dim.Points[2].Y + dim.Y, 3); // still 30 above the shape
        Assert.Equal(textBefore, DimensionRenderer.Layout(dim).Text);

        ShapeOutline.SetBox(box, movedLeft, movedTop, ShapeOutline.Box(box).W * 2, ShapeOutline.Box(box).H);
        DimensionRouter.Refresh(document);

        Assert.NotEqual(textBefore, DimensionRenderer.Layout(dim).Text); // longer now
        var right = ConnectionRouter.GetPoint(box, 4);
        Assert.Equal(right.X, dim.Points[1].X + dim.X, 3);
        Assert.Equal(movedTop - 30, dim.Points[2].Y + dim.Y, 3);
    }

    [Fact]
    public void ADimensionMovedByHand_LetsGo()
    {
        var document = new Document { Width = 600, Height = 400 };
        var layer = new Layer { Name = "L" };
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 120 };
        layer.Shapes.Add(box);
        document.Layers.Add(layer);
        var a = ConnectionRouter.GetPoint(box, 0);
        var b = ConnectionRouter.GetPoint(box, 4);
        var dim = new Shape { Kind = ShapeKind.Dimension, DimKind = DimensionKind.Horizontal, X = a.X, Y = a.Y };
        dim.Points = [new CorePoint(0, 0), new CorePoint(b.X - a.X, 0), new CorePoint(0, -30)];
        PathBoundsCalculator.Recompute(dim);
        layer.Shapes.Add(dim);
        DimensionRouter.AttachPoints(document, dim);
        DimensionRouter.Refresh(document);

        dim.X += 25; // dragged away
        DimensionRouter.Refresh(document);
        box.X += 40;
        var xAfterRelease = dim.X;
        DimensionRouter.Refresh(document);

        Assert.Equal(xAfterRelease, dim.X, 3);
    }
}

public class DimensionLookTests
{
    private static Shape Aligned(double length = 200)
    {
        var dim = new Shape { Kind = ShapeKind.Dimension, DimKind = DimensionKind.Aligned, X = 0, Y = 50 };
        DimensionBuilder.ApplyStyle(dim);
        dim.Points = [new CorePoint(0, 0), new CorePoint(length, 0), new CorePoint(0, 30)];
        PathBoundsCalculator.Recompute(dim);
        return dim;
    }

    [Fact]
    public void TheNumberSitsOnTheMiddleOfTheLine_WhichIsBrokenBehindIt()
    {
        var layout = DimensionRenderer.Layout(Aligned());

        // middle of the dimension line (x = 100, y = 80) - the number is centred exactly there ...
        Assert.Equal(100, layout.TextPosition.X, 1);
        Assert.Equal(80, layout.TextPosition.Y, 1);

        // ... and no line runs through it: the line is two pieces with a gap of the number's width
        using var measure = new SKPathMeasure(layout.Lines, false);
        var pieces = 0;
        do
        {
            pieces++;
        }
        while (measure.NextContour());
        // two line pieces + two extension lines
        Assert.Equal(4, pieces);
        Assert.False(layout.Lines.Contains(100f, 80f));
        var gapHalf = layout.TextWidth / 2;
        Assert.True(gapHalf > 5);
    }

    [Fact]
    public void ANumberThatDoesNotFit_GoesAboveTheLine()
    {
        var layout = DimensionRenderer.Layout(Aligned(length: 40));

        Assert.True(layout.TextPosition.Y < 80, "above the line");
    }

    [Fact]
    public void SizesCanBeChosen()
    {
        var dim = Aligned();
        var defaults = DimensionStyle.For(dim);

        dim.DimFontSize = 30;
        dim.DimLineWidth = 3;
        dim.DimArrowSize = 20;
        dim.DimExtGap = 10;
        dim.DimExtOvershoot = 12;
        var chosen = DimensionStyle.For(dim);

        Assert.Equal(30, chosen.FontSize);
        Assert.Equal(3, chosen.LineWidth);
        Assert.Equal(20, chosen.ArrowSize);
        Assert.Equal(10, chosen.ExtGap);
        Assert.Equal(12, chosen.ExtOvershoot);
        Assert.NotEqual(defaults.FontSize, chosen.FontSize);

        var layout = DimensionRenderer.Layout(dim);
        Assert.Equal(20, layout.ArrowLength);
        Assert.True(layout.TextWidth > DimensionRenderer.Layout(Aligned()).TextWidth, "bigger number is wider");
    }

    [Fact]
    public void TheExtensionLinesStartAfterTheGapAndRunPastTheDimensionLine()
    {
        var dim = Aligned();
        dim.DimExtGap = 8;
        dim.DimExtOvershoot = 10;

        var layout = DimensionRenderer.Layout(dim);
        using var measure = new SKPathMeasure(layout.Lines, false);

        // the first contour is the extension line of the first point: from y = 50 + gap to y = 80 + overshoot
        var bounds = layout.Lines.Bounds;
        Assert.Equal(50 + 8, bounds.Top, 1);
        Assert.Equal(80 + 10, bounds.Bottom, 1);
    }

    [Fact]
    public void WallDimensions_UseTheSameLook()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 20, Y = 60, WallThickness = 10, DimOffset = 14, DimFontSize = 18,
            Points = [new CorePoint(0, 0), new CorePoint(160, 0)],
        };
        PathBoundsCalculator.Recompute(wall);

        using var bitmap = new SKBitmap(220, 160);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        ShapeRenderer.Draw(canvas, wall);

        var reds = new List<string>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 150 && pixel.Green < 120 && pixel.Blue < 120)
                {
                    reds.Add($"{x},{y}");
                }
            }
        }

        Assert.True(reds.Count > 20, $"red dimension pixels: {reds.Count}");
        Assert.Contains(reds, r => r.EndsWith(",79") || r.EndsWith(",80") || r.EndsWith(",78"));
    }
}
