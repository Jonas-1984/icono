using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class MeasureTests
{
    private static Shape Box(double x, double y, double w, double h)
    {
        var shape = new Shape { Kind = ShapeKind.Rectangle, X = x, Y = y, Width = w, Height = h };
        return shape;
    }

    private static Shape HorizontalWall(double x1, double x2, double y, double thickness = 10)
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = x1, Y = y, WallThickness = thickness,
            Points = [new CorePoint(0, 0), new CorePoint(x2 - x1, 0)],
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static Document DocumentWith(params Shape[] shapes)
    {
        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        foreach (var shape in shapes)
        {
            layer.Shapes.Add(shape);
        }

        document.Layers.Add(layer);
        return document;
    }

    [Fact]
    public void AWallHasTheCornersOfBothFaces()
    {
        var wall = HorizontalWall(100, 400, 200, thickness: 10);

        var corners = OutlineGeometry.Corners(wall);

        Assert.Equal(4, corners.Count);
        Assert.Contains(corners, c => Math.Abs(c.X - 100) < 0.01 && Math.Abs(c.Y - 195) < 0.01);
        Assert.Contains(corners, c => Math.Abs(c.X - 400) < 0.01 && Math.Abs(c.Y - 205) < 0.01);
        Assert.Equal(4, OutlineGeometry.EdgeMiddles(wall).Count);
    }

    [Fact]
    public void TheGapBetweenAShapeAndAWall_IsTheShortestDistance()
    {
        var wall = HorizontalWall(100, 500, 300, thickness: 20); // its upper face is at y = 290
        var shape = Box(200, 100, 100, 60);
        var outline = ShapeOutline.Box(shape);

        var gap = OutlineGeometry.ClosestPoints(shape, wall);

        Assert.NotNull(gap);
        // straight down from the shape's lower edge to the wall's upper face
        Assert.Equal(290 - (outline.Y + outline.H), gap!.Value.Distance, 0);
        Assert.Equal(290, gap.Value.B.Y, 1);
    }

    [Fact]
    public void ShapesThatTouchAreZeroApart()
    {
        var a = Box(100, 100, 100, 100);
        var b = Box(100, 100, 100, 100);

        var gap = OutlineGeometry.ClosestPoints(a, b);

        Assert.Equal(0, gap!.Value.Distance, 1);
    }

    [Fact]
    public void ADimensionBetweenTwoObjects_MeasuresAndFollowsThem()
    {
        var wall = HorizontalWall(100, 500, 300, thickness: 20);
        var shape = Box(200, 100, 100, 60);
        var document = DocumentWith(wall, shape);

        var dim = DimensionBuilder.CreateGap(shape, wall)!;
        document.Layers[0].Shapes.Add(dim);
        DimensionRouter.Refresh(document);
        var before = DimensionRenderer.Layout(dim).Text;

        shape.Y += 40; // closer to the wall
        DimensionRouter.Refresh(document);
        var after = DimensionRenderer.Layout(dim).Text;

        Assert.Equal(DimensionRole.Gap, dim.DimRole);
        Assert.NotEqual(before, after);
        Assert.Equal(shape.Id, dim.DimTarget);
        Assert.Equal(wall.Id, dim.DimTarget2);

        var expected = 290 - (ShapeOutline.Box(shape).Y + ShapeOutline.Box(shape).H);
        Assert.Equal(DimensionRenderer.Format(expected, LengthUnit.Centimeter, 2, scale: 1), after);
    }

    [Fact]
    public void AGapDimension_IsFreedWhenOneOfTheObjectsIsDeleted()
    {
        var wall = HorizontalWall(100, 500, 300);
        var shape = Box(200, 100, 100, 60);
        var document = DocumentWith(wall, shape);
        var dim = DimensionBuilder.CreateGap(shape, wall)!;
        document.Layers[0].Shapes.Add(dim);
        DimensionRouter.Refresh(document);

        document.Layers[0].Shapes.Remove(wall);
        DimensionRouter.Refresh(document);

        Assert.Null(dim.DimTarget);
    }

    [Fact]
    public void TheSnapperFindsCornersMiddlesCentresAndEdges()
    {
        var wall = HorizontalWall(100, 400, 200, thickness: 10);
        var document = DocumentWith(wall);

        var corner = MeasureSnapper.Find(document, new SKPoint(102, 193), 6)!;
        var edge = MeasureSnapper.Find(document, new SKPoint(250, 206), 6)!;
        var nothing = MeasureSnapper.Find(document, new SKPoint(250, 260), 6);

        Assert.Equal(MeasureSnapKind.Corner, corner.Kind);
        Assert.Equal(100, corner.Point.X, 1);
        Assert.Equal(195, corner.Point.Y, 1);
        Assert.Equal(MeasureSnapKind.Midpoint, edge.Kind); // x = 250 is the middle of the lower face
        Assert.Equal(205, edge.Point.Y, 1);
        Assert.Null(nothing);

        var onEdge = MeasureSnapper.Find(document, new SKPoint(170, 207), 6)!;
        Assert.Equal(MeasureSnapKind.Edge, onEdge.Kind);
        Assert.Equal(170, onEdge.Point.X, 1);
        Assert.Equal(205, onEdge.Point.Y, 1);
    }

    [Fact]
    public void ADimensionPinnedToAWallFace_FollowsTheWall()
    {
        var wall = HorizontalWall(100, 400, 200, thickness: 10);
        var shape = Box(150, 50, 60, 40);
        var document = DocumentWith(wall, shape);
        var outline = ShapeOutline.Box(shape);

        // measure from the shape's bottom-left corner down to the wall's upper-left face corner
        var dim = new Shape { Kind = ShapeKind.Dimension, DimKind = DimensionKind.Vertical, X = outline.X, Y = outline.Y + outline.H };
        DimensionBuilder.ApplyStyle(dim);
        dim.Points = [new CorePoint(0, 0), new CorePoint(100 - outline.X, 195 - (outline.Y + outline.H)), new CorePoint(-20, 0)];
        PathBoundsCalculator.Recompute(dim);
        document.Layers[0].Shapes.Add(dim);

        DimensionRouter.AttachPoints(document, dim);
        Assert.NotNull(dim.DimAnchors);
        Assert.NotNull(dim.DimAnchors![0]);
        Assert.NotNull(dim.DimAnchors[1]);
        Assert.True(dim.DimAnchors[1]!.Value.Index >= OutlineGeometry.CornerBase, "the wall face corner is an outline anchor");

        DimensionRouter.Refresh(document);
        wall.Y += 50; // move the wall down: the dimension gets longer
        DimensionRouter.Refresh(document);

        var movedY = dim.Points![1].Y + dim.Y;
        Assert.Equal(195 + 50, movedY, 1);
    }
}

public class WallDimensionRemapTests
{
    private static Shape Wall(bool closed)
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 0, Y = 0, WallThickness = 10, WallClosed = closed,
            Points = [new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 100), new CorePoint(0, 100)],
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    [Fact]
    public void AddingAPoint_KeepsTheSettingsOnTheirSegments()
    {
        var wall = Wall(closed: false); // segments 0, 1, 2
        wall.WallDimensionOverrides = [new WallDimensionOverride(0, Hidden: true), new WallDimensionOverride(2, Flip: true)];

        WallEditor.RemapOverridesAfterInsert(wall, 1); // segment 1 is split: old segment 2 becomes 3

        Assert.Equal([0, 3], wall.WallDimensionOverrides!.Select(o => o.Segment).ToArray());
    }

    [Fact]
    public void RemovingAPoint_DropsTheMergedSegmentsSettingsAndMovesTheRest()
    {
        var wall = Wall(closed: false);
        wall.WallDimensionOverrides =
            [new WallDimensionOverride(0, Hidden: true), new WallDimensionOverride(1, Flip: true), new WallDimensionOverride(2, Text: "x")];

        WallEditor.RemapOverridesBeforeRemove(wall, 1); // the corner between segment 0 and 1 goes

        Assert.Equal([0, 1], wall.WallDimensionOverrides!.Select(o => o.Segment).ToArray());
        Assert.Equal("x", wall.WallDimensionOverrides[1].Text);
    }

    [Fact]
    public void RemovingTheFirstOrLastPointOfAnOpenWall()
    {
        var first = Wall(closed: false);
        first.WallDimensionOverrides = [new WallDimensionOverride(0, Hidden: true), new WallDimensionOverride(1, Flip: true)];
        WallEditor.RemapOverridesBeforeRemove(first, 0);
        Assert.Equal([0], first.WallDimensionOverrides!.Select(o => o.Segment).ToArray());
        Assert.True(first.WallDimensionOverrides[0].Flip);

        var last = Wall(closed: false);
        last.WallDimensionOverrides = [new WallDimensionOverride(1, Flip: true), new WallDimensionOverride(2, Hidden: true)];
        WallEditor.RemapOverridesBeforeRemove(last, 3);
        Assert.Equal([1], last.WallDimensionOverrides!.Select(o => o.Segment).ToArray());
    }

    [Fact]
    public void OpeningARing_PrunesTheClosingSegmentsSettings()
    {
        var wall = Wall(closed: true); // segments 0..3, the last one closes the ring
        wall.WallDimensionOverrides = [new WallDimensionOverride(3, Hidden: true), new WallDimensionOverride(1, Flip: true)];

        wall.WallClosed = false;
        WallEditor.PruneOverrides(wall);

        Assert.Equal([1], wall.WallDimensionOverrides!.Select(o => o.Segment).ToArray());
    }

    [Fact]
    public void AnOutlineAnchorThatNoLongerExists_IsLetGo()
    {
        var wall = Wall(closed: false);
        var shape = new Shape { Kind = ShapeKind.Rectangle, X = 200, Y = 0, Width = 60, Height = 40 };
        var document = new Document { Width = 500, Height = 400 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        layer.Shapes.Add(shape);
        document.Layers.Add(layer);

        var corners = OutlineGeometry.Corners(wall);
        Assert.True(corners.Count > 4, "the bent wall has more outline corners than a straight one");
        var corner = corners[^1];
        var dim = new Shape { Kind = ShapeKind.Dimension, DimKind = DimensionKind.Horizontal, X = corner.X, Y = corner.Y };
        dim.Points = [new CorePoint(0, 0), new CorePoint(200 - corner.X, 0), new CorePoint(0, -30)];
        PathBoundsCalculator.Recompute(dim);
        layer.Shapes.Add(dim);
        DimensionRouter.AttachPoints(document, dim);
        Assert.NotNull(dim.DimAnchors?[0]);

        // the wall becomes a single short segment: its last corner number does not exist any more
        wall.Points = [new CorePoint(0, 0), new CorePoint(50, 0)];
        PathBoundsCalculator.Recompute(wall);
        DimensionRouter.Refresh(document);

        Assert.True(dim.DimAnchors is null || dim.DimAnchors[0] is null);
    }

    [Fact]
    public void GeometryIsCachedUntilTheShapeChanges()
    {
        var wall = Wall(closed: false);
        var first = OutlineGeometry.Corners(wall);
        var again = OutlineGeometry.Corners(wall);
        Assert.Same(first, again);

        wall.WallThickness = 30;
        var changed = OutlineGeometry.Corners(wall);
        Assert.NotSame(first, changed);
        Assert.NotEqual(first[0].Y, changed[0].Y);
    }
}
