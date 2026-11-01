using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>The Handwriting tool: points of a pen stroke become one smooth, movable, resizable stroke.</summary>
public class FreehandTests
{
    private static List<SKPoint> Wave()
    {
        var points = new List<SKPoint>();
        for (var x = 0; x <= 200; x += 2)
        {
            points.Add(new SKPoint(100 + x, 100 + 30 * MathF.Sin(x / 25f)));
        }

        return points;
    }

    [Fact]
    public void Simplify_KeepsTheEndsAndTheBends_AndDropsThePointsOnTheLine()
    {
        var straight = Enumerable.Range(0, 50).Select(i => new SKPoint(i * 2, 10)).ToList();
        Assert.Equal(2, FreehandBuilder.Simplify(straight, 0.5f).Count);

        var corner = straight.Concat(Enumerable.Range(1, 50).Select(i => new SKPoint(98, 10 + i * 2))).ToList();
        var simple = FreehandBuilder.Simplify(corner, 0.5f);
        Assert.Equal(3, simple.Count);
        Assert.Equal(98, simple[1].X);

        var wave = Wave();
        Assert.InRange(FreehandBuilder.Simplify(wave, 0.8f).Count, 5, wave.Count - 10);
    }

    [Fact]
    public void CreateShape_MakesAStrokeShape_WithTheFrameOfTheStroke_ColourAndWidth()
    {
        var color = ArgbColor.FromRgb(200, 30, 30);

        var shape = FreehandBuilder.CreateShape(Wave(), color, 3.5, 0.8f)!;

        Assert.True(shape.Freehand);
        Assert.Equal(ShapeKind.CombinedPath, shape.Kind);
        Assert.True(shape.CustomPathStrokeOnly);
        Assert.False(shape.FillEnabled);
        Assert.Equal(color, shape.BorderColor);
        Assert.Equal(3.5, shape.BorderThickness);
        Assert.InRange(shape.X, 99, 101);
        Assert.InRange(shape.Width, 195, 205);

        // drawn, the stroke lies where it was written
        using var path = ShapeGeometryFactory.Build(shape);
        Assert.InRange(path.Bounds.Left, 99, 102);
        Assert.InRange(path.Bounds.Right, 298, 302);
    }

    [Fact]
    public void ADotAndAStraightStroke_AreStrokesToo_NoPointsGivesNothing()
    {
        Assert.Null(FreehandBuilder.CreateShape([], ArgbColor.FromRgb(0, 0, 0), 2, 0.8f));

        var dot = FreehandBuilder.CreateShape([new SKPoint(50, 60)], ArgbColor.FromRgb(0, 0, 0), 6, 0.8f)!;
        Assert.True(dot.Width >= 1 && dot.Height >= 1);

        var line = FreehandBuilder.CreateShape([new SKPoint(10, 10), new SKPoint(110, 10)], ArgbColor.FromRgb(0, 0, 0), 2, 0.8f)!;
        Assert.True(line.Height >= 1);
        using var path = ShapeGeometryFactory.Build(line);
        Assert.Equal(100, path.Bounds.Width, 1);
    }

    [Fact]
    public void HitTest_IsOnTheStrokeOnly_NotInsideItsFrame()
    {
        var shape = FreehandBuilder.CreateShape(Wave(), ArgbColor.FromRgb(0, 0, 0), 3, 0.8f)!;

        Assert.True(FreehandBuilder.HitTest(shape, 100, 100, 2));
        Assert.True(FreehandBuilder.HitTest(shape, 200, 100 + 30 * MathF.Sin(4f), 2));
        Assert.False(FreehandBuilder.HitTest(shape, 200, 100 + 29 * MathF.Sin(4f) + 25, 2)); // in the frame, far from the stroke
    }

    [Fact]
    public void AStroke_ResizesWithItsFrame_AndKeepsTheFlagWhenCopied()
    {
        var shape = FreehandBuilder.CreateShape(Wave(), ArgbColor.FromRgb(0, 0, 0), 3, 0.8f)!;
        shape.Width *= 2;

        using var path = ShapeGeometryFactory.Build(shape);
        Assert.InRange(path.Bounds.Width, 390, 410);

        var copy = new Shape { Kind = ShapeKind.CombinedPath };
        ShapeSnapshot.Capture(shape).ApplyTo(copy);
        Assert.True(copy.Freehand);
    }
}
