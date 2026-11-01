using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class SketchAndHeadSizeTests
{
    private static Shape Arrow(bool sketch = false, double? head = null)
    {
        var line = new Shape
        {
            Kind = ShapeKind.Arrow, X = 20, Y = 20, BorderThickness = 3, BorderColor = ArgbColor.FromRgb(0, 0, 0),
            LineEndHead = ArrowHeadKind.Open, Sketch = sketch, LineHeadSize = head,
        };
        line.Points = [new CorePoint(0, 80), new CorePoint(60, 70), new CorePoint(100, 0)];
        PathBoundsCalculator.Recompute(line);
        return line;
    }

    private static SKBitmap Render(Shape shape)
    {
        var bitmap = new SKBitmap(160, 140);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        ShapeRenderer.Draw(canvas, shape);
        return bitmap;
    }

    private static int Dark(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void HeadSize_ScalesTheHead_AndNullKeepsTheUsualSize()
    {
        using var usual = ShapeGeometryFactory.Build(Arrow());
        using var same = ShapeGeometryFactory.Build(Arrow(head: ShapeGeometryFactory.NominalHeadSize(ArrowHeadKind.Open)));
        using var big = ShapeGeometryFactory.Build(Arrow(head: 21));

        Assert.Equal(usual.ToSvgPathData(), same.ToSvgPathData());
        Assert.NotEqual(usual.ToSvgPathData(), big.ToSvgPathData());
    }

    [Fact]
    public void HeadSize_AlsoAppliesToLoopArrows()
    {
        var small = new Shape { Kind = ShapeKind.LoopArrowClockwise, Width = 100, Height = 100, LineEndHead = ArrowHeadKind.Filled, LineHeadSize = 4 };
        var large = new Shape { Kind = ShapeKind.LoopArrowClockwise, Width = 100, Height = 100, LineEndHead = ArrowHeadKind.Filled, LineHeadSize = 20 };

        using var a = ShapeGeometryFactory.Build(small);
        using var b = ShapeGeometryFactory.Build(large);

        Assert.NotEqual(a.ToSvgPathData(), b.ToSvgPathData());
    }

    [Fact]
    public void Sketch_DrawsTheLineByHand_ItIsDifferentFromTheCleanLine_AndStillThere()
    {
        using var clean = Render(Arrow());
        using var sketch = Render(Arrow(sketch: true));

        Assert.True(Dark(sketch) > 100);
        Assert.NotEqual(Dark(clean), Dark(sketch));
    }

    [Fact]
    public void Sketch_OfAClosedShape_IsDrawnToo()
    {
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 20, Y = 20, Width = 100, Height = 80, FillEnabled = false, BorderThickness = 2, BorderColor = ArgbColor.FromRgb(0, 0, 0), Sketch = true };

        using var bitmap = Render(box);

        Assert.True(Dark(bitmap) > 200);
    }

    [Fact]
    public void BothNewSettings_AreCarriedByTheSnapshot()
    {
        var shape = Arrow(sketch: true, head: 12);
        var copy = new Shape { Kind = ShapeKind.Arrow };
        ShapeSnapshot.Capture(shape).ApplyTo(copy);

        Assert.True(copy.Sketch);
        Assert.Equal(12, copy.LineHeadSize);
    }
}
