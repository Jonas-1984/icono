using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

/// <summary>The Curve tool rounds the corners of a line or a shape with the corner radius.</summary>
public class CurveCornersTests
{
    private static Shape Polyline(params (double X, double Y)[] points)
    {
        var line = new Shape { Kind = ShapeKind.Polyline, X = points[0].X, Y = points[0].Y };
        line.Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList();
        PathBoundsCalculator.Recompute(line);
        return line;
    }

    [Fact]
    public void CanRoundCorners_IsTrueForLinesWithCorners_AndStraightEdgeShapes()
    {
        Assert.True(ShapeGeometryFactory.CanRoundCorners(Polyline((0, 0), (100, 0), (100, 100))));
        Assert.True(ShapeGeometryFactory.CanRoundCorners(new Shape { Kind = ShapeKind.Rectangle, Width = 50, Height = 50 }));
        Assert.True(ShapeGeometryFactory.CanRoundCorners(new Shape { Kind = ShapeKind.Star, Width = 50, Height = 50 }));
    }

    [Fact]
    public void CanRoundCorners_IsFalse_WhereThereIsNothingToCurve()
    {
        Assert.False(ShapeGeometryFactory.CanRoundCorners(Polyline((0, 0), (100, 0))));
        Assert.False(ShapeGeometryFactory.CanRoundCorners(new Shape { Kind = ShapeKind.Ellipse, Width = 50, Height = 50 }));
        Assert.False(ShapeGeometryFactory.CanRoundCorners(new Shape { Kind = ShapeKind.Wall, Points = [new CorePoint(0, 0), new CorePoint(9, 0), new CorePoint(9, 9)] }));
        var smooth = Polyline((0, 0), (100, 0), (100, 100));
        smooth.PathRoutingStyle = PathRoutingStyle.Curved;
        Assert.False(ShapeGeometryFactory.CanRoundCorners(smooth));
    }

    [Fact]
    public void ACornerRadius_CutsTheCornerOfAPolyline()
    {
        var sharp = Polyline((0, 0), (100, 0), (100, 100));
        var curved = Polyline((0, 0), (100, 0), (100, 100));
        curved.CornerRadius = 30;

        using var sharpPath = ShapeGeometryFactory.Build(sharp);
        using var curvedPath = ShapeGeometryFactory.Build(curved);

        // the curve replaces the sharp corner by more points on the path
        Assert.NotEqual(sharpPath.PointCount, curvedPath.PointCount);
    }

    [Fact]
    public void AClosedShapeMadeOfPoints_IsRoundedToo()
    {
        var square = new Shape { Kind = ShapeKind.Rectangle };
        square.Points = [new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 100), new CorePoint(0, 100)];
        PathBoundsCalculator.Recompute(square);
        using var sharp = ShapeGeometryFactory.Build(square);

        square.CornerRadius = 20;
        using var rounded = ShapeGeometryFactory.Build(square);

        Assert.NotEqual(sharp.PointCount, rounded.PointCount);
    }
}
