using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>A dimension line that is dragged snaps onto a corner of the drawing - the face of a neighbouring wall, the edge of a piece of
/// furniture - when no other dimension line is near, so it runs exactly along that corner.</summary>
[Collection("Scale")]
public class DimensionLineCornerSnapTests : IDisposable
{
    public void Dispose() => DrawingScale.Denominator = 1;

    private static Shape Wall(float y, float length)
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = 100, Y = y, WallThickness = 16, WallShowDimensions = false, Points = [new CorePoint(0, 0), new CorePoint(length, 0)] };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static Document Document(params Shape[] shapes)
    {
        DrawingScale.Denominator = 1;
        var document = new Document { Width = 1000, Height = 800 };
        var layer = new Layer { Name = "L" };
        foreach (var shape in shapes)
        {
            layer.Shapes.Add(shape);
        }

        document.Layers.Add(layer);
        return document;
    }

    [Fact]
    public void TheLine_SnapsOntoTheEdgeOfAnObject_AcrossTheLineOnly()
    {
        var wall = Wall(100, 300);
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 150, Y = 300, Width = 80, Height = 60 };
        var document = Document(wall, box);

        var edge = SnapGeometry.Of(box).Bounds.Top;   // the outline of a rectangle lies inside its frame
        var snap = DimensionLineSnapper.Snap(document, wall, 0, new SKPoint(200, edge + 5), new SKPoint(1, 0), 8)!;

        Assert.NotNull(snap);
        Assert.Equal(edge, snap.Point.Y, 3);
        Assert.Equal(200, snap.Point.X, 3);   // along the line the pointer stays where it is
        Assert.Equal(edge, snap.From.Y, 3);   // the guide runs along the edge
        Assert.Equal(edge, snap.To.Y, 3);
    }

    [Fact]
    public void TheLine_SnapsOntoTheFaceOfAnotherWall()
    {
        var wall = Wall(100, 300);
        var other = Wall(400, 200);   // its faces lie 8 above and below its centre line: y 392 and 408
        var document = Document(wall, other);

        var snap = DimensionLineSnapper.Snap(document, wall, 0, new SKPoint(200, 395), new SKPoint(1, 0), 8)!;

        Assert.NotNull(snap);
        Assert.Equal(392, snap.Point.Y, 3);
    }

    [Fact]
    public void OutOfReach_OrOnlyTheOwnShape_NothingSnaps()
    {
        var wall = Wall(100, 300);
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 150, Y = 300, Width = 80, Height = 60 };

        Assert.Null(DimensionLineSnapper.Snap(Document(wall, box), wall, 0, new SKPoint(200, SnapGeometry.Of(box).Bounds.Top + 20), new SKPoint(1, 0), 8));
        Assert.Null(DimensionLineSnapper.Snap(Document(wall), wall, 0, new SKPoint(200, 110), new SKPoint(1, 0), 8));
    }

    [Fact]
    public void ADimensionLine_IsPreferredToACorner()
    {
        var first = Wall(100, 300);
        first.WallShowDimensions = true;
        var second = Wall(300, 200);
        var row = WallRenderer.LayoutSegmentDimension(first, 0)!.LineFrom.Y;
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 150, Y = row + 4, Width = 80, Height = 60 };

        var snap = DimensionLineSnapper.Snap(Document(first, second, box), second, 0, new SKPoint(200, row + 2), new SKPoint(1, 0), 8)!;

        Assert.Equal(row, snap.Point.Y, 3);   // not the box edge 4 further
    }
}
