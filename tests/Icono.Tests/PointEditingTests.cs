using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Tests;

/// <summary>Add Point / Delete Point: a point goes into the nearest side, the nearest point goes out - each one undo step.</summary>
public class PointEditingTests
{
    private static (EditorContext Context, Shape Line) Setup(params (double X, double Y)[] points)
    {
        var context = new EditorContext();
        context.Document.Layers.Add(new Layer { Name = "L" });
        var line = new Shape { Kind = ShapeKind.Polyline, X = 100, Y = 100, Points = points.Select(p => new CorePoint(p.X, p.Y)).ToList() };
        PathBoundsCalculator.Recompute(line);
        context.Document.Layers[0].Shapes.Add(line);
        return (context, line);
    }

    [Fact]
    public void APoint_GoesIntoTheNearestSide_OnTheSide()
    {
        var (context, line) = Setup((0, 0), (100, 0), (100, 100));

        var done = PointEditing.InsertPointNear(context, line, 150, 104, tolerance: 10);

        Assert.True(done);
        Assert.Equal(4, line.Points!.Count);
        Assert.Equal(50, line.Points[1].X, 3);   // the new point lies on the first side, in the middle
        Assert.Equal(0, line.Points[1].Y, 3);
    }

    [Fact]
    public void FarFromEverySide_NothingIsInserted()
    {
        var (context, line) = Setup((0, 0), (100, 0));

        Assert.False(PointEditing.InsertPointNear(context, line, 150, 150, tolerance: 10));
        Assert.Equal(2, line.Points!.Count);
    }

    [Fact]
    public void InsertingIsOneUndoStep()
    {
        var (context, line) = Setup((0, 0), (100, 0));
        PointEditing.InsertPointNear(context, line, 150, 100, tolerance: 10);

        context.Undo();

        Assert.Equal(2, line.Points!.Count);
    }

    [Fact]
    public void TheNearestPoint_IsTakenOut()
    {
        var (context, line) = Setup((0, 0), (50, 0), (100, 0));

        var done = PointEditing.RemovePointNear(context, line, 150, 102, tolerance: 10);

        Assert.True(done);
        Assert.Equal(2, line.Points!.Count);
        Assert.Equal(100, line.Points[1].X, 3);   // the middle one is gone
        context.Undo();
        Assert.Equal(3, line.Points!.Count);
    }

    [Fact]
    public void TwoPointsStay_AndALockedShapeIsLeftAlone()
    {
        var (context, line) = Setup((0, 0), (100, 0));
        Assert.False(PointEditing.RemovePointNear(context, line, 100, 100, tolerance: 10));

        var (context2, locked) = Setup((0, 0), (50, 0), (100, 0));
        locked.Locked = true;
        Assert.False(PointEditing.RemovePointNear(context2, locked, 100, 100, tolerance: 10));
        Assert.False(PointEditing.InsertPointNear(context2, locked, 125, 100, tolerance: 10));
    }
}
