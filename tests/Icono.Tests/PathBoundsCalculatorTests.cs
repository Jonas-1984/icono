using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class PathBoundsCalculatorTests
{
    [Fact]
    public void AddingPointLeftOfOrigin_ShiftsXYAndReAnchorsExistingPoints()
    {
        var shape = new Shape
        {
            Kind = ShapeKind.Polyline,
            X = 10,
            Y = 10,
            Points = [new CorePoint(0, 0), new CorePoint(20, 20)],
        };

        // Add a new point at absolute (0,0) — shape-local (-10,-10) relative to the current origin.
        var list = shape.Points.ToList();
        list.Add(new CorePoint(-10, -10));
        shape.Points = list;

        PathBoundsCalculator.Recompute(shape);

        Assert.Equal(0, shape.X);
        Assert.Equal(0, shape.Y);
        Assert.Equal(30, shape.Width);
        Assert.Equal(30, shape.Height);

        // The original first point (absolute (10,10)) must now read as local (10,10) from the new origin.
        Assert.Equal(new CorePoint(10, 10), shape.Points[0]);
        Assert.Equal(new CorePoint(30, 30), shape.Points[1]);
        Assert.Equal(new CorePoint(0, 0), shape.Points[2]);
    }

    [Fact]
    public void SinglePointDegenerateShape_ClampsToMinimumOneByOne()
    {
        var shape = new Shape { Kind = ShapeKind.Polyline, X = 5, Y = 5, Points = [new CorePoint(0, 0)] };

        PathBoundsCalculator.Recompute(shape);

        Assert.Equal(1, shape.Width);
        Assert.Equal(1, shape.Height);
    }

    [Fact]
    public void NullOrEmptyPoints_LeavesBoundingBoxUntouched()
    {
        var shape = new Shape { Kind = ShapeKind.Rectangle, X = 3, Y = 4, Width = 50, Height = 60 };

        PathBoundsCalculator.Recompute(shape);

        Assert.Equal(3, shape.X);
        Assert.Equal(4, shape.Y);
        Assert.Equal(50, shape.Width);
        Assert.Equal(60, shape.Height);
    }
}
