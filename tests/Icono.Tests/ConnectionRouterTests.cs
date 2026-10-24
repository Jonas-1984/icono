using Icono.Core;
using Icono.IO;
using Icono.Rendering;

namespace Icono.Tests;

public class ConnectionRouterTests
{
    private static (Document Document, Shape Box, Shape Line) Build()
    {
        var document = new Document { Width = 400, Height = 300 };
        var layer = new Layer { Name = "L" };
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 80, Height = 40 };
        // The arrow starts free at (20,20) and ends exactly on the box's top-left connection point.
        var (ax, ay) = ConnectionRouter.GetPoint(box, 0);
        var line = new Shape
        {
            Kind = ShapeKind.Arrow, X = 20, Y = 20, Width = ax - 20, Height = ay - 20,
            Points = [new CorePoint(0, 0), new CorePoint(ax - 20, ay - 20)],
        };
        layer.Shapes.Add(box);
        layer.Shapes.Add(line);
        document.Layers.Add(layer);
        return (document, box, line);
    }

    private static SkiaSharp.SKRect Outline(Shape shape)
    {
        using var path = ShapeGeometryFactory.Build(shape);
        return path.TightBounds;
    }

    [Fact]
    public void ConnectionPoints_AreTheFourCornersAndThreePointsPerSide_OnTheDrawnOutline()
    {
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 80, Height = 40 };
        var o = Outline(box);

        Assert.Equal(16, ConnectionRouter.PointCount);
        AssertPoint(o.Left, o.Top, ConnectionRouter.GetPoint(box, 0));
        AssertPoint(o.Left + o.Width / 2, o.Top, ConnectionRouter.GetPoint(box, 2));
        AssertPoint(o.Right, o.Bottom, ConnectionRouter.GetPoint(box, 8));
        AssertPoint(o.Left, o.Top + o.Height * 0.75, ConnectionRouter.GetPoint(box, 13));
    }

    private static void AssertPoint(double x, double y, (double X, double Y) actual)
    {
        Assert.Equal(x, actual.X, 3);
        Assert.Equal(y, actual.Y, 3);
    }

    [Fact]
    public void ConnectionPoints_FollowTheShapesRotation()
    {
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 100, Height = 100, Rotation = 90 };
        var o = Outline(new Shape { Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = 100, Height = 100 });

        var (x, y) = ConnectionRouter.GetPoint(box, 2); // top middle turns into right middle

        Assert.Equal(50 + o.Height / 2, x, 3);
        Assert.Equal(50, y, 3);
    }

    [Fact]
    public void FindNearest_ReturnsThePointWithinReachOnly()
    {
        var (document, box, _) = Build();

        var (px, py) = ConnectionRouter.GetPoint(box, 2);
        var near = ConnectionRouter.FindNearest(document, px + 1, py - 1, tolerance: 5);
        var far = ConnectionRouter.FindNearest(document, 300, 250, tolerance: 5);

        Assert.NotNull(near);
        Assert.Same(box, near!.Value.Shape);
        Assert.Equal(2, near.Value.Index);
        Assert.Null(far);
    }

    [Fact]
    public void AttachedEnd_FollowsTheTargetWhenItMoves()
    {
        var (document, box, line) = Build();
        ConnectionRouter.AttachEnds(document, line);
        Assert.Equal(new ShapeAnchor(box.Id, 0), line.EndAnchor);
        Assert.Null(line.StartAnchor);

        var (ax, ay) = ConnectionRouter.GetPoint(box, 0);
        box.X += 50;
        box.Y += 30;
        ConnectionRouter.Refresh(document);

        var end = line.Points![^1];
        Assert.Equal(ax + 50, line.X + end.X, 3);
        Assert.Equal(ay + 30, line.Y + end.Y, 3);
        var start = line.Points![0];
        Assert.Equal(20, line.X + start.X, 6); // the free end stays where it was
        Assert.Equal(20, line.Y + start.Y, 6);
    }

    [Fact]
    public void AttachedEnd_IsFreedWhenTheLineIsMovedByHand()
    {
        var (document, box, line) = Build();
        ConnectionRouter.AttachEnds(document, line);
        Assert.NotNull(line.EndAnchor);

        line.X += 25;
        ConnectionRouter.Refresh(document);

        Assert.Null(line.EndAnchor);
        var endX = line.X + line.Points![^1].X;
        box.X += 40;
        ConnectionRouter.Refresh(document);
        Assert.Equal(endX, line.X + line.Points![^1].X, 6); // no longer follows
    }

    [Fact]
    public void AttachedEnd_IsFreedWhenTheTargetIsDeleted()
    {
        var (document, box, line) = Build();
        ConnectionRouter.AttachEnds(document, line);

        document.Layers[0].Shapes.Remove(box);
        ConnectionRouter.Refresh(document);

        Assert.Null(line.EndAnchor);
    }

    [Fact]
    public void Anchors_SurviveSaveAndLoad()
    {
        var (document, box, line) = Build();
        ConnectionRouter.AttachEnds(document, line);

        var path = Path.Combine(Path.GetTempPath(), $"icono-conn-{Guid.NewGuid():N}{DocumentFile.Extension}");
        var loaded = new Document();
        try
        {
            DocumentFile.Save(document, path);
            DocumentFile.Load(path).ApplyTo(loaded);
        }
        finally
        {
            File.Delete(path);
        }

        var loadedBox = loaded.Layers[0].Shapes[0];
        var loadedLine = loaded.Layers[0].Shapes[1];
        Assert.Equal(box.Id, loadedBox.Id);
        Assert.Equal(new ShapeAnchor(box.Id, 0), loadedLine.EndAnchor);

        ConnectionRouter.Refresh(loaded); // the first redraw after opening a file
        var endBefore = loadedLine.X + loadedLine.Points![^1].X;
        loadedBox.X += 10;
        ConnectionRouter.Refresh(loaded);
        Assert.Equal(endBefore + 10, loadedLine.X + loadedLine.Points![^1].X, 6);
    }
}
