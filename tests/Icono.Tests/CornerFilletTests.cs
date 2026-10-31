using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>The Curve tool of a CAD program (fillet): one corner at a time, chosen by its two sides.</summary>
public class CornerFilletTests
{
    private static Shape Box() => new() { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 100 };

    [Fact]
    public void ARectangleHasFourCornersAndFourSides()
    {
        var vertices = CornerFillet.Vertices(Box())!;

        Assert.Equal(4, vertices.Count);
        Assert.True(CornerFillet.IsClosed(Box()));
        Assert.Equal(4, CornerFillet.SideCount(4, closed: true));
    }

    [Fact]
    public void NearestSide_FindsTheSideUnderThePointer_OnlyWhenClose()
    {
        var box = Box();
        var vertices = CornerFillet.Vertices(box)!;
        var (a, b) = CornerFillet.Ends(vertices, 0);
        var middle = new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        Assert.Equal(0, CornerFillet.NearestSide(box, new SKPoint(middle.X, middle.Y + 2), 6));
        Assert.Null(CornerFillet.NearestSide(box, new SKPoint(200, 150), 6)); // inside, far from every side
    }

    [Fact]
    public void SharedCorner_IsWhereTwoNeighbouringSidesMeet()
    {
        Assert.Equal(1, CornerFillet.SharedCorner(4, true, 0, 1));
        Assert.Equal(0, CornerFillet.SharedCorner(4, true, 3, 0)); // the closing side meets the first one at corner 0
        Assert.Null(CornerFillet.SharedCorner(4, true, 0, 2)); // opposite sides
        Assert.Null(CornerFillet.SharedCorner(4, true, 1, 1));
        Assert.Equal(1, CornerFillet.SharedCorner(4, false, 0, 1));
        Assert.Null(CornerFillet.SharedCorner(2, false, 0, 1)); // no corner at the end of an open line
    }

    [Fact]
    public void Apply_CurvesJustThatCorner_AndTurnsARectangleIntoPoints()
    {
        var box = Box();
        using var sharp = ShapeGeometryFactory.Build(box);

        Assert.True(CornerFillet.Apply(box, 1, 30));

        Assert.NotNull(box.Points);
        Assert.Equal([0, 30, 0, 0], box.PointRadii);
        using var curved = ShapeGeometryFactory.Build(box);
        Assert.NotEqual(sharp.PointCount, curved.PointCount);

        // only corner 1 is cut: the outline still reaches the other three corners, but not corner 1
        var vertices = CornerFillet.Vertices(box)!;
        Assert.True(curved.Contains(vertices[0].X + 0.6f, vertices[0].Y + 0.6f));
        Assert.True(curved.Contains(vertices[2].X - 0.6f, vertices[2].Y - 0.6f));
        Assert.True(curved.Contains(vertices[3].X + 0.6f, vertices[3].Y - 0.6f));
        Assert.False(curved.Contains(vertices[1].X - 0.6f, vertices[1].Y + 0.6f));
    }

    [Fact]
    public void Apply_WithZero_MakesTheCornerSharpAgain_AndRadiiFollowPointEdits()
    {
        var box = Box();
        CornerFillet.Apply(box, 2, 20);
        Assert.True(CornerFillet.Apply(box, 2, 0));
        Assert.Null(box.PointRadii);

        Assert.Equal([0, 0, 10, 0, 0], CornerFillet.AfterInsert([0, 10, 0, 0], 4, 0));
        Assert.Equal([0, 10, 0], CornerFillet.AfterRemove([0, 10, 0, 0], 4, 3));
        Assert.Null(CornerFillet.AfterRemove([0, 10, 0, 0], 4, 1));
    }

    [Fact]
    public void ACornerOfAnOpenLine_CanBeCurved_ItsEndsCannotBeChosenAsCorners()
    {
        var line = new Shape { Kind = ShapeKind.Polyline, X = 0, Y = 0 };
        line.Points = [new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 100)];
        PathBoundsCalculator.Recompute(line);

        Assert.False(CornerFillet.Apply(line, 5, 10));
        Assert.True(CornerFillet.Apply(line, 1, 25));
        Assert.Equal([0, 25, 0], line.PointRadii);
    }
}
