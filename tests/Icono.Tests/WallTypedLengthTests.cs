using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class WallTypedLengthTests
{
    private static Shape NewWall()
    {
        var wall = new Shape { Kind = ShapeKind.Wall };
        WallEditor.ApplyDrawingDefaults(wall, 40);
        return wall;
    }

    /// <summary>Plays what the editor does while a wall is typed: the pointer gives the direction, the number the length, Enter places the corner.</summary>
    private static List<SKPoint> Draw(Shape wall, double[] typed, (double X, double Y)[] directions, bool closeAtEnd)
    {
        var points = new List<SKPoint> { new(300, 600), new(300, 600) };
        for (var k = 0; k < typed.Length; k++)
        {
            var (ux, uy) = directions[k];
            if (k > 0)
            {
                WallTypedLength.KeepPreviousTyped(wall, points, typed[k - 1], ux, uy);
            }

            points[^1] = WallTypedLength.EndFor(wall, points, ux, uy, typed[k]);
            var last = k == typed.Length - 1;
            var gap = Math.Sqrt(Math.Pow(points[^1].X - points[0].X, 2) + Math.Pow(points[^1].Y - points[0].Y, 2));
            if (closeAtEnd && last && points.Count - 1 >= 3 && gap <= wall.WallThickness * 1.3)
            {
                var start = WallTypedLength.CloseRoom(wall, points, typed[k]);
                points[0] = start;
                points.RemoveAt(points.Count - 1);
                return points;
            }

            if (!last)
            {
                points.Add(points[^1]);
            }
        }

        WallTypedLength.FitLast(wall, points, typed[^1]);
        return points;
    }

    [Fact]
    public void ANewWall_IsFifteenCentimetresThickAtTheDrawingScale()
    {
        var wall = NewWall();

        Assert.Equal(15 / 2.54 * 96 / 40, wall.WallThickness, 6);
    }

    [Fact]
    public void ATypedLengthIsTheLengthTheDimensionShows_ForALineOfWalls()
    {
        var wall = NewWall();
        var typed = new[] { 300.0, 400.0 };

        var points = Draw(wall, typed, [(0, -1), (1, 0)], closeAtEnd: false);

        Assert.Equal(3, points.Count);
        Assert.Equal(typed[0], WallTypedLength.Measured(wall, points, 0), 2);
        Assert.Equal(typed[1], WallTypedLength.Measured(wall, points, 1), 2);
    }

    [Fact]
    public void ATypedRoom_ClosesAndEveryWallShowsItsTypedInsideLength()
    {
        var wall = NewWall();
        var typed = new[] { 300.0, 400.0, 300.0, 400.0 };

        var points = Draw(wall, typed, [(0, -1), (1, 0), (0, 1), (-1, 0)], closeAtEnd: true);

        Assert.Equal(4, points.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(typed[i], WallTypedLength.Measured(wall, points, i, closed: true), 2);
        }

        // and it is a rectangle
        Assert.Equal(points[0].X, points[1].X, 2);
        Assert.Equal(points[2].X, points[3].X, 2);
        Assert.Equal(points[1].Y, points[2].Y, 2);
        Assert.Equal(points[3].Y, points[0].Y, 2);
    }

    [Fact]
    public void ACentreMeasure_TakesTheTypedNumberAsTheCentreLine()
    {
        var wall = NewWall();
        wall.WallDimensionMeasure = WallMeasure.Centre;

        var points = Draw(wall, [300, 400], [(0, -1), (1, 0)], closeAtEnd: false);

        Assert.Equal(300, Math.Abs(points[1].Y - points[0].Y), 2);
        Assert.Equal(400, Math.Abs(points[2].X - points[1].X), 2);
    }
}
