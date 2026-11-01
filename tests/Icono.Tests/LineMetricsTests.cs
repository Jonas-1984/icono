using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class LineMetricsTests
{
    private static Shape Line(params (double X, double Y)[] points)
    {
        var line = new Shape { Kind = points.Length == 2 ? ShapeKind.Line : ShapeKind.Polyline, X = points[0].X, Y = points[0].Y };
        line.Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList();
        PathBoundsCalculator.Recompute(line);
        return line;
    }

    [Fact]
    public void Length_OfALine_IsTheDistanceBetweenItsPoints_AndOfAPolyline_TheSumOfItsSides()
    {
        Assert.Equal(100, LineMetrics.Length(Line((0, 0), (60, 80))), 3);
        Assert.Equal(200, LineMetrics.Length(Line((0, 0), (100, 0), (100, 100))), 3);
    }

    [Fact]
    public void SetLength_MovesTheEndAlongTheLine_AndKeepsTheStart()
    {
        var line = Line((10, 20), (70, 100));

        Assert.True(LineMetrics.SetLength(line, 250));

        Assert.Equal(250, LineMetrics.Length(line), 3);
        var points = WallGeometry.AbsolutePoints(line);
        Assert.Equal(10, points[0].X, 3);
        Assert.Equal(20, points[0].Y, 3);
        // still pointing the same way (3 : 4)
        Assert.Equal(0.6, (points[1].X - points[0].X) / 250, 3);
        Assert.Equal(0.8, (points[1].Y - points[0].Y) / 250, 3);
    }

    [Fact]
    public void SetLength_ScalesAPolylineAsAWhole()
    {
        var line = Line((0, 0), (100, 0), (100, 100));

        Assert.True(LineMetrics.SetLength(line, 100));

        Assert.Equal(100, LineMetrics.Length(line), 3);
        Assert.Equal(50, line.Width, 3);
        Assert.Equal(50, line.Height, 3);
    }

    [Fact]
    public void SetLength_WithRoundedCorners_StillReachesTheLength()
    {
        var line = Line((0, 0), (100, 0), (100, 100));
        line.CornerRadius = 30;

        Assert.True(LineMetrics.SetLength(line, 500));
        Assert.Equal(500, LineMetrics.Length(line), 2);
    }

    [Fact]
    public void ALoopArrow_KeepsItsSizeAndItsStart_ItsLineGetsShorterOrLonger()
    {
        var loop = new Shape { Kind = ShapeKind.LoopArrowClockwise, X = 100, Y = 100, Width = 100, Height = 100 };
        var before = LineMetrics.Length(loop);
        var start = WallGeometryStart(loop);

        Assert.True(LineMetrics.SetLength(loop, before / 2));

        Assert.Equal(before / 2, LineMetrics.Length(loop), 2);
        Assert.Equal(100, loop.Width, 6); // the loop itself did not become smaller
        Assert.Equal(start.X, WallGeometryStart(loop).X, 2);
        Assert.Equal(start.Y, WallGeometryStart(loop).Y, 2);
        Assert.True(loop.LoopSweep < ShapeGeometryFactory.ClassicLoopSweep);

        Assert.True(LineMetrics.SetLength(loop, before * 1.2));
        Assert.Equal(before * 1.2, LineMetrics.Length(loop), 2);
        Assert.True(loop.LoopSweep > ShapeGeometryFactory.ClassicLoopSweep);
    }

    [Fact]
    public void ALoopArrow_NeverGoesBeyondTheFullCircle_ALongerLineMakesTheCircleBigger()
    {
        var loop = new Shape { Kind = ShapeKind.LoopArrowCounterClockwise, X = 100, Y = 100, Width = 100, Height = 100 };
        var full = LineMetrics.Length(new Shape { Kind = ShapeKind.LoopArrowCounterClockwise, Width = 100, Height = 100, LoopSweep = ShapeGeometryFactory.MaxLoopSweep });

        Assert.True(LineMetrics.SetLength(loop, full * 2));

        Assert.Equal(ShapeGeometryFactory.MaxLoopSweep, loop.LoopSweep);
        Assert.Equal(full * 2, LineMetrics.Length(loop), 2);
        Assert.True(loop.Width > 100);
    }

    private static SkiaSharp.SKPoint WallGeometryStart(Shape loop)
    {
        using var path = ShapeGeometryFactory.Build(loop);
        using var measure = new SkiaSharp.SKPathMeasure(path, false);
        measure.GetPositionAndTangent(0, out var position, out _);
        return position;
    }

    [Fact]
    public void SetLength_RefusesWhatIsNotALine()
    {
        Assert.False(LineMetrics.SetLength(new Shape { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 }, 50));
        Assert.False(LineMetrics.SetLength(Line((0, 0), (10, 0)), 0));
    }
}
