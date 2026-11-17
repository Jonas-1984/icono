using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class FreehandCornerTests
{
    /// <summary>A line drawn with a point every <paramref name="step"/> px.</summary>
    private static IEnumerable<SKPoint> Line(SKPoint from, SKPoint to, float step = 2)
    {
        var length = Geo.Distance(from, to);
        var count = Math.Max(1, (int)(length / step));
        for (var i = 0; i < count; i++)
        {
            var t = i / (float)count;
            yield return new SKPoint(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
        }
    }

    /// <summary>The tip of an arrow head: down to the tip, and up again at a sharp angle.</summary>
    private static List<SKPoint> VShape()
    {
        var points = Line(new SKPoint(0, 0), new SKPoint(50, 100)).ToList();
        points.AddRange(Line(new SKPoint(50, 100), new SKPoint(100, 0)));
        points.Add(new SKPoint(100, 0));
        return points;
    }

    private static List<SKPoint> Circle(float radius)
    {
        var points = new List<SKPoint>();
        var steps = (int)(2 * MathF.PI * radius / 2);
        for (var i = 0; i <= steps; i++)
        {
            var angle = 2 * MathF.PI * i / steps;
            points.Add(new SKPoint(200 + radius * MathF.Cos(angle), 200 + radius * MathF.Sin(angle)));
        }

        return points;
    }

    [Fact]
    public void TheTipOfAnArrow_IsASharpCorner()
    {
        var points = VShape();

        var corners = FreehandBuilder.DetectCorners(points, window: 10);

        var tip = Assert.Single(corners);
        Assert.InRange(points[tip].Y, 96, 100); // the point where the pen turned round
    }

    [Theory]
    [InlineData(40)]
    [InlineData(15)]
    [InlineData(6)] // a small tight curl, like the loop of a letter
    public void ARoundCurve_HasNoCorner_HoweverTightItIs(float radius)
    {
        Assert.Empty(FreehandBuilder.DetectCorners(Circle(radius), window: 10));
    }

    [Fact]
    public void AZigzag_HasACornerAtEveryTurn()
    {
        var points = Line(new SKPoint(0, 0), new SKPoint(60, 0)).ToList();
        points.AddRange(Line(new SKPoint(60, 0), new SKPoint(0, 60)));
        points.AddRange(Line(new SKPoint(0, 60), new SKPoint(60, 60)));
        points.Add(new SKPoint(60, 60));

        Assert.Equal(2, FreehandBuilder.DetectCorners(points, window: 10).Count); // a Z
    }

    [Fact]
    public void TheSmoothedTip_ReachesTheCorner_InsteadOfCuttingItOff()
    {
        var points = VShape();

        using var sharp = FreehandBuilder.BuildPath(points, tolerance: 0.8f, smoothing: 4.5f);
        using var rounded = FreehandBuilder.BuildPath(points, tolerance: 0.8f, smoothing: 4.5f, keepCorners: false);

        // with the corner kept the stroke reaches the tip (y = 100); smoothed away it stays clearly short of it
        Assert.InRange(sharp.TightBounds.Bottom, 99, 101);
        Assert.True(rounded.TightBounds.Bottom < sharp.TightBounds.Bottom - 2);
    }

    [Fact]
    public void ARealHandStroke_WithTremorAndAPauseAtTheTip_StillHasOneSharpTip()
    {
        var random = new Random(7);
        float Shake() => (float)(random.NextDouble() - 0.5) * 1.2f; // the hand shakes by about half a pixel

        // down to the tip, the pen rests there (several points almost on top of each other), and up again
        var points = Line(new SKPoint(0, 0), new SKPoint(50, 100), 1.5f).ToList();
        for (var i = 0; i < 6; i++)
        {
            points.Add(new SKPoint(50, 100));
        }

        points.AddRange(Line(new SKPoint(50, 100), new SKPoint(100, 0), 1.5f));
        points.Add(new SKPoint(100, 0));
        var shaky = points.Select(p => new SKPoint(p.X + Shake(), p.Y + Shake())).ToList();

        var corners = FreehandBuilder.DetectCorners(shaky, window: 9.6f);
        Assert.Single(corners);

        using var path = FreehandBuilder.BuildPath(shaky, tolerance: 0.8f, smoothing: 4.5f);
        Assert.InRange(path.TightBounds.Bottom, 98.5, 101.5);
    }

    [Fact]
    public void ASmoothCurve_IsStillSmoothed_AsOneStroke()
    {
        using var path = FreehandBuilder.BuildPath(Circle(40), tolerance: 0.8f, smoothing: 4.5f);

        // no corner: one piece, the ends of a circle meet where it started
        Assert.InRange(path.TightBounds.Width, 78, 82);
        Assert.InRange(path.TightBounds.Height, 78, 82);
    }
}
