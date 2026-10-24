using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class WallTests
{
    private static Shape MakeWall(bool closed, params (double X, double Y)[] points)
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = points[0].X, Y = points[0].Y, WallThickness = 10, WallClosed = closed,
            Hatch = HatchKind.CrossHatch, HatchSpacing = 6,
            Points = points.Select(p => new CorePoint(p.X - points[0].X, p.Y - points[0].Y)).ToList(),
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    [Fact]
    public void Outline_IsABandOfTheWallThicknessAroundTheCentreLine()
    {
        var wall = MakeWall(false, (0, 50), (100, 50));

        using var outline = WallGeometry.BuildOutline(wall);

        Assert.Equal(0, outline.Bounds.Left, 3);
        Assert.Equal(100, outline.Bounds.Right, 3);
        Assert.Equal(45, outline.Bounds.Top, 3);
        Assert.Equal(55, outline.Bounds.Bottom, 3);
    }

    [Fact]
    public void ClosedWall_IsARingWithAHole()
    {
        var wall = MakeWall(true, (0, 0), (100, 0), (100, 100), (0, 100));

        using var outline = WallGeometry.BuildOutline(wall);

        Assert.True(outline.Contains(50, 0), "on the wall");
        Assert.False(outline.Contains(50, 50), "inside the room");
        Assert.Equal(4, WallGeometry.Segments(wall).Count);
    }

    [Fact]
    public void Lengths_AreWrittenInTheChosenUnit()
    {
        Assert.Equal("2.54 cm", WallGeometry.FormatLength(96, LengthUnit.Centimeter));
        Assert.Equal("25.4 mm", WallGeometry.FormatLength(96, LengthUnit.Millimeter));
        Assert.Equal("1 in", WallGeometry.FormatLength(96, LengthUnit.Inch));
        Assert.Equal("0.025 m", WallGeometry.FormatLength(96, LengthUnit.Meter));
    }

    [Fact]
    public void Cut_InTheMiddleOfASegment_MakesTwoWallsThatKeepTheLook()
    {
        var wall = MakeWall(false, (0, 0), (100, 0), (100, 80));

        var spot = WallEditor.Nearest(wall, 40, 3, vertexReach: 5)!.Value;
        var parts = WallEditor.SplitAt(wall, spot);

        Assert.Equal(2, parts.Count);
        Assert.Equal(40, parts[0].X + parts[0].Points![^1].X, 3);
        Assert.Equal(40, parts[1].X, 3);
        Assert.Equal(3, parts[1].Points!.Count); // cut point, corner, end
        Assert.All(parts, p => Assert.Equal(HatchKind.CrossHatch, p.Hatch));
        Assert.All(parts, p => Assert.Equal(10, p.WallThickness));
    }

    [Fact]
    public void Cut_AtACorner_SplitsThereAndTheEndsOfAnOpenWallCannotBeCut()
    {
        var wall = MakeWall(false, (0, 0), (100, 0), (100, 80));

        var corner = WallEditor.Nearest(wall, 99, 1, vertexReach: 5)!.Value;
        var end = WallEditor.Nearest(wall, 0, 0, vertexReach: 5)!.Value;

        Assert.True(corner.IsVertex);
        Assert.Equal(2, WallEditor.SplitAt(wall, corner).Count);
        Assert.Empty(WallEditor.SplitAt(wall, end));
    }

    [Fact]
    public void Cut_AClosedWall_OpensItAtThatPlace()
    {
        var wall = MakeWall(true, (0, 0), (100, 0), (100, 100), (0, 100));

        var spot = WallEditor.Nearest(wall, 50, 1, vertexReach: 5)!.Value;
        var parts = WallEditor.SplitAt(wall, spot);

        var opened = Assert.Single(parts);
        Assert.False(opened.WallClosed);
        Assert.Equal(6, opened.Points!.Count); // cut, three corners... and back to the cut
        var first = opened.Points[0];
        var last = opened.Points[^1];
        Assert.Equal(opened.X + first.X, opened.X + last.X, 3);
    }

    [Fact]
    public void SplitAtAllPoints_GivesOneWallPerSegment()
    {
        var open = MakeWall(false, (0, 0), (100, 0), (100, 80), (0, 80));
        var ring = MakeWall(true, (0, 0), (100, 0), (100, 80), (0, 80));

        Assert.Equal(3, WallEditor.SplitAtAllPoints(open).Count);
        Assert.Equal(4, WallEditor.SplitAtAllPoints(ring).Count);
    }

    [Fact]
    public void ResizingAWall_StretchesItsPoints()
    {
        var wall = MakeWall(false, (10, 10), (110, 10), (110, 60));

        ShapeOutline.SetBox(wall, 10, 10, 200, 100);

        Assert.Equal(200, wall.Width, 3);
        Assert.Equal(100, wall.Height, 3);
        Assert.Equal(200, wall.Points![1].X - wall.Points[0].X, 3);
    }

    [Fact]
    public void WallsSurviveUndoSnapshotsAndRender()
    {
        var wall = MakeWall(true, (0, 0), (60, 0), (60, 60), (0, 60));
        var snapshot = ShapeSnapshot.Capture(wall);
        var copy = new Shape { Kind = ShapeKind.Wall };
        snapshot.ApplyTo(copy);

        Assert.True(copy.WallClosed);
        Assert.Equal(HatchKind.CrossHatch, copy.Hatch);

        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        ShapeRenderer.Draw(canvas, wall);
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(30, 0)); // the wall's top band is painted
    }
}
