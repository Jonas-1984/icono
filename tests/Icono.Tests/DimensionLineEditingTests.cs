using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class DimensionLineEditingTests
{
    private static (Document Document, Shape Wall) Room()
    {
        var document = new Document { Width = 800, Height = 800 };
        var layer = new Layer { Name = "Layer 1" };
        var wall = WallRooms.Build(RoomPreset.Closed, 400, 400, 400, 300, 50);
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);
        return (document, wall);
    }

    /// <summary>How far, across its own direction, a point lies from the dimension line (signed: the side matters).</summary>
    private static float SideOf(DimensionLayout line, SKPoint point)
    {
        var dx = line.LineTo.X - line.LineFrom.X;
        var dy = line.LineTo.Y - line.LineFrom.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        return ((point.X - line.LineFrom.X) * dy - (point.Y - line.LineFrom.Y) * dx) / length;
    }

    [Fact]
    public void ThePlacedDimensionLine_PassesThroughThePointItWasDraggedTo()
    {
        var (document, wall) = Room();
        var before = WallRenderer.LayoutSegmentDimension(wall, 0)!;
        var middle = new SKPoint((before.LineFrom.X + before.LineTo.X) / 2, (before.LineFrom.Y + before.LineTo.Y) / 2);
        var direction = DimensionLineEditing.Direction(wall, 0, null)!.Value;
        var across = new SKPoint(-direction.Y, direction.X);
        var length = MathF.Sqrt(across.X * across.X + across.Y * across.Y);
        var target = new SKPoint(middle.X + across.X / length * 90, middle.Y + across.Y / length * 90);

        Assert.True(DimensionLineEditing.Place(document, wall, 0, target));

        var after = WallRenderer.LayoutSegmentDimension(wall, 0)!;
        Assert.Equal(0, SideOf(after, target), 1);
    }

    [Fact]
    public void DraggingToTheOtherSideOfTheWall_FlipsTheDimension()
    {
        var (document, wall) = Room();
        var a = wall.Points![0];
        var b = wall.Points[1];
        var middle = new SKPoint((float)(wall.X + (a.X + b.X) / 2), (float)(wall.Y + (a.Y + b.Y) / 2)); // the middle of the wall segment itself
        var direction = DimensionLineEditing.Direction(wall, 0, null)!.Value;
        var across = new SKPoint(-direction.Y, direction.X);
        var length = MathF.Sqrt(across.X * across.X + across.Y * across.Y);
        SKPoint At(float distance) => new(middle.X + across.X / length * distance, middle.Y + across.Y / length * distance);

        // where the line lies across the wall (a fixed direction: a flipped dimension also runs the other way along its own line)
        float Across(DimensionLayout line) =>
            ((line.LineFrom.X + line.LineTo.X) / 2 - middle.X) * across.X / length + ((line.LineFrom.Y + line.LineTo.Y) / 2 - middle.Y) * across.Y / length;

        DimensionLineEditing.Place(document, wall, 0, At(80));
        var one = Across(WallRenderer.LayoutSegmentDimension(wall, 0)!);

        DimensionLineEditing.Place(document, wall, 0, At(-80));
        var other = Across(WallRenderer.LayoutSegmentDimension(wall, 0)!);

        // the line follows the pointer to the other side, at the same distance
        Assert.Equal(80, one, 1);
        Assert.Equal(-80, other, 1);
    }
    [Fact]
    public void TheDirection_FollowsTheWallSegment()
    {
        var (_, wall) = Room();

        var direction = DimensionLineEditing.Direction(wall, 0, null)!.Value;
        var a = wall.Points![0];
        var b = wall.Points[1];

        // along the first segment of the room (parallel to it)
        var cross = direction.X * (b.Y - a.Y) - direction.Y * (b.X - a.X);
        Assert.Equal(0, cross, 1);
    }

    [Fact]
    public void ADimensionLineThatRunsTheSameWayAsAnother_SnapsOntoIt()
    {
        var (document, wall) = Room();
        // a second wall piece beside the first one, with its dimension line somewhere else
        var other = WallRooms.Build(RoomPreset.Closed, 400, 400, 400, 300, 50);
        document.Layers[0].Shapes.Add(other);
        var mine = WallRenderer.LayoutSegmentDimension(wall, 0)!;
        var theirs = WallRenderer.LayoutSegmentDimension(other, 0)!;
        var direction = DimensionLineEditing.Direction(wall, 0, null)!.Value;

        // a pointer a little beside the other dimension line is pulled onto it
        var near = new SKPoint(theirs.LineFrom.X + 2, theirs.LineFrom.Y + 2);
        var landed = DimensionLineEditing.Snapped(document, wall, 0, near, direction, tolerance: 8, out var snap);

        Assert.NotNull(snap);
        Assert.Equal(0, SideOf(theirs, landed), 1);
        Assert.NotEqual(near, landed);
        _ = mine;
    }
}
