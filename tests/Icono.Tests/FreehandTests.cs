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

    /// <summary>A line along y = 100 that a shaking hand drew: the pen jumps up and down by 2 px, every second point.</summary>
    private static List<SKPoint> ShakyLine() =>
        Enumerable.Range(0, 101).Select(i => new SKPoint(50 + i * 3, 100 + (i % 2 == 0 ? 2f : -2f))).ToList();

    [Fact]
    public void Stabilize_TakesTheTremorOut_AndKeepsBothEndsWherePenWentDownAndUp()
    {
        var shaky = ShakyLine();

        var steady = FreehandBuilder.Stabilize(shaky, 3f);

        Assert.Equal(shaky[0], steady[0]);
        Assert.Equal(shaky[^1], steady[^1]);
        // the middle of the line is straight now (the shake was 2 px), only the first and last points still carry the pen's own start and end
        Assert.All(steady.Skip(8).Take(steady.Count - 16), p => Assert.InRange(p.Y, 99.5f, 100.5f));
    }

    [Fact]
    public void Stabilize_DoesNotChangeAStraightLine_AShortStroke_OrNoSmoothing()
    {
        var straight = Enumerable.Range(0, 40).Select(i => new SKPoint(i * 5, 20)).ToList();
        Assert.All(FreehandBuilder.Stabilize(straight, 3f), p => Assert.Equal(20, p.Y, 3));

        var two = new List<SKPoint> { new(0, 0), new(10, 10) };
        Assert.Equal(two, FreehandBuilder.Stabilize(two, 3f));

        var shaky = ShakyLine();
        Assert.Equal(shaky, FreehandBuilder.Stabilize(shaky, 0f));
    }

    [Fact]
    public void Stabilize_KeepsTheShapeOfTheWriting_OnlyRoundsTheFinestDetail()
    {
        var wave = Wave();

        var steady = FreehandBuilder.Stabilize(wave, 3f);

        // a wave of 157 px length and 30 px height is not tremor: its crest is still there
        Assert.InRange(steady.Max(p => p.Y) - steady.Min(p => p.Y), 55, 61);
    }

    [Fact]
    public void CreateShape_WithSmoothing_MakesTheShakyLineStraight_AndSmaller()
    {
        var color = ArgbColor.FromRgb(0, 0, 0);

        var rough = FreehandBuilder.CreateShape(ShakyLine(), color, 2, 0.8f)!;
        var smooth = FreehandBuilder.CreateShape(ShakyLine(), color, 2, 0.8f, 3f)!;

        Assert.InRange(rough.Height, 3.5, 4.5);
        Assert.InRange(smooth.Height, 1, 2.5);
        Assert.True(smooth.CustomPathData!.Length < rough.CustomPathData!.Length);
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
