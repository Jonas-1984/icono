using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>A dimension is placed with the mouse alone: the line goes through the point the pointer is at, on either side of the wall, at any
/// distance - no "inside / outside" or "distance" settings are needed.</summary>
public class DimensionPlacementTests
{
    private static Shape StraightWall()
    {
        DrawingScale.Denominator = 1;
        var wall = new Shape { Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16, Points = [new CorePoint(0, 0), new CorePoint(400, 0)] };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    /// <summary>The y of the dimension line of segment 0 as it is drawn (the tips of its arrows lie on the line).</summary>
    private static float LineY(Shape wall) => WallRenderer.LayoutSegmentDimension(wall, 0)!.Arrows[0].Tip.Y;

    private static void PlaceThrough(Shape wall, float y)
    {
        var line = WallRenderer.DimensionLineThrough(wall, 0, new SKPoint(300, y))!.Value;
        wall.WallDimensionOverrides = [new WallDimensionOverride(0, Offset: line.Offset, Flip: line.Flip)];
    }

    [Theory]
    [InlineData(40f)]   // far above the wall
    [InlineData(70f)]
    [InlineData(150f)]  // below the wall
    [InlineData(230f)]
    public void TheWallDimensionLine_RunsThroughThePointOfTheMouse_OnEitherSide(float y)
    {
        var wall = StraightWall();

        PlaceThrough(wall, y);

        Assert.Equal(y, LineY(wall), 0.5);
    }

    [Fact]
    public void TheTwoSidesOfTheWall_NeedOppositeSides_AndTheSameDistanceFromTheFace()
    {
        var wall = StraightWall();

        var above = WallRenderer.DimensionLineThrough(wall, 0, new SKPoint(300, 60))!.Value;   // 40 from the centre line
        var below = WallRenderer.DimensionLineThrough(wall, 0, new SKPoint(300, 140))!.Value;

        Assert.NotEqual(above.Flip, below.Flip);
        Assert.Equal(32, above.Offset, 3); // 40 - half the thickness (8)
        Assert.Equal(32, below.Offset, 3);
    }

    [Fact]
    public void ApointOnTheWall_GivesDistanceZero_NotANegativeOne()
    {
        var wall = StraightWall();

        var line = WallRenderer.DimensionLineThrough(wall, 0, new SKPoint(300, 102))!.Value;

        Assert.Equal(0, line.Offset);
    }

    [Fact]
    public void ASegmentThatDoesNotExist_HasNoPlace()
    {
        Assert.Null(WallRenderer.DimensionLineThrough(StraightWall(), 5, new SKPoint(0, 0)));
    }

    private static (Document Document, Shape Window) RoomWithWindow()
    {
        DrawingScale.Denominator = 1;
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallClosed = true, WallThickness = 16,
            Points = [new CorePoint(0, 0), new CorePoint(400, 0), new CorePoint(400, 300), new CorePoint(0, 300)],
        };
        PathBoundsCalculator.Recompute(wall);
        var window = new Shape { Kind = ShapeKind.WindowSingle, X = 150, Y = 92, Width = 80, Height = 16, WallThickness = 16 };
        ArchitecturalSymbols.ApplyDefaults(window);

        var document = new Document { Width = 800, Height = 600 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(window);
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);
        return (document, window);
    }

    private static float WidthLineY(Document document, Shape window) =>
        SymbolMeasurements.Layout(document, window).Single(d => d.Part == SymbolDimensionPart.Width).Layout.Arrows[0].Tip.Y;

    [Theory]
    [InlineData(30f)]   // outside the room, above the top wall
    [InlineData(60f)]
    [InlineData(200f)]  // inside the room
    [InlineData(260f)]
    public void TheWindowDimensionLine_RunsThroughThePointOfTheMouse_OutsideAndInsideTheRoom(float y)
    {
        var (document, window) = RoomWithWindow();

        Assert.True(SymbolMeasurements.PlaceDimensionLine(document, window, new SKPoint(190, y)));

        Assert.Equal(y, WidthLineY(document, window), 0.5);
    }

    [Fact]
    public void InsideTheRoom_TheWindowDimensionStaysClearOfTheWallsOwnDimensionLine()
    {
        var (document, window) = RoomWithWindow();

        SymbolMeasurements.PlaceDimensionLine(document, window, new SKPoint(190, 112)); // right at the wall

        // the wall dimension lines run 28 px from the face (at 108); the window's sit 14 px further in
        Assert.True(WidthLineY(document, window) >= 108 + 28 + 14 - 0.5, $"y={WidthLineY(document, window)}");
    }

    [Fact]
    public void TheWindowDimension_SwitchesSideWithThePointer_AndBackAgain()
    {
        var (document, window) = RoomWithWindow();

        SymbolMeasurements.PlaceDimensionLine(document, window, new SKPoint(190, 40));
        var outside = window.WallDimensionPlacement;
        SymbolMeasurements.PlaceDimensionLine(document, window, new SKPoint(190, 170));
        var inside = window.WallDimensionPlacement;
        SymbolMeasurements.PlaceDimensionLine(document, window, new SKPoint(190, 40));

        Assert.NotEqual(outside, inside);
        Assert.Equal(outside, window.WallDimensionPlacement);
    }

    [Fact]
    public void OnlyDoorsWindowsAndOpenings_CanHaveTheirDimensionPlaced()
    {
        var (document, _) = RoomWithWindow();

        Assert.False(SymbolMeasurements.PlaceDimensionLine(document, new Shape { Kind = ShapeKind.Rectangle }, new SKPoint(0, 0)));
    }
}

/// <summary>Dimension lines snap onto each other so that dimensions stand in one row, flush.</summary>
public class DimensionLineSnapTests
{
    private static Shape Wall(double y, double length)
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = y, WallThickness = 16,
            Points = [new CorePoint(0, 0), new CorePoint(length, 0)],
        };
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

    private static float LineCoordinate(Shape wall) => WallRenderer.LayoutSegmentDimension(wall, 0)!.LineFrom.Y;

    [Fact]
    public void ADimensionLine_SnapsOntoTheLineOfAnotherDimension_SoTheyStandInOneRow()
    {
        var first = Wall(100, 300);
        var second = Wall(300, 200);
        var document = Document(first, second);
        var row = LineCoordinate(first);

        var snap = DimensionLineSnapper.Snap(document, second, 0, new SKPoint(150, row + 5), new SKPoint(1, 0), 8)!;

        Assert.Equal(row, snap.Point.Y, 3);
        Assert.Equal(150, snap.Point.X, 3);              // only across the line, not along it
        Assert.Equal(row, snap.From.Y, 3);                // the guide lies on the line it lined up with
        Assert.Equal(row, snap.To.Y, 3);
    }

    [Fact]
    public void TheSnappedLine_IsFlushWithTheOther_WhenTheDimensionIsPlacedThere()
    {
        var first = Wall(100, 300);
        var second = Wall(300, 200);
        var document = Document(first, second);
        var row = LineCoordinate(first);

        var snap = DimensionLineSnapper.Snap(document, second, 0, new SKPoint(150, row - 4), new SKPoint(1, 0), 8)!;
        var line = WallRenderer.DimensionLineThrough(second, 0, snap.Point)!.Value;
        second.WallDimensionOverrides = [new WallDimensionOverride(0, Offset: line.Offset, Flip: line.Flip)];

        Assert.Equal(row, LineCoordinate(second), 0.5);
    }

    [Fact]
    public void FarAway_ParallelToNothing_OrOnlyItself_NothingSnaps()
    {
        var first = Wall(100, 300);
        var second = Wall(300, 200);
        var document = Document(first, second);
        var row = LineCoordinate(first);

        Assert.Null(DimensionLineSnapper.Snap(document, second, 0, new SKPoint(150, row + 30), new SKPoint(1, 0), 8));   // out of reach
        Assert.Null(DimensionLineSnapper.Snap(document, second, 0, new SKPoint(150, row + 2), new SKPoint(0, 1), 8));    // runs another way
        Assert.Null(DimensionLineSnapper.Snap(Document(first), first, 0, new SKPoint(150, row + 2), new SKPoint(1, 0), 8)); // only itself there
    }

    [Fact]
    public void TheDimensionsOfTheSameWall_SnapToEachOther_AndNearestWins()
    {
        // an L: two segments one after the other along the same side, with the first dimension pushed further out
        DrawingScale.Denominator = 1;
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16,
            Points = [new CorePoint(0, 0), new CorePoint(200, 0), new CorePoint(200, 100), new CorePoint(400, 100)],
        };
        PathBoundsCalculator.Recompute(wall);
        wall.WallDimensionOverrides = [new WallDimensionOverride(0, Offset: 60)];
        var document = Document(wall);
        var layouts = WallRenderer.AllSegmentLayouts(wall);
        var firstLine = layouts[0]!.LineFrom.Y;
        var thirdLine = layouts[2]!.LineFrom.Y;

        var toFirst = DimensionLineSnapper.Snap(document, wall, 2, new SKPoint(300, firstLine + 3), new SKPoint(1, 0), 8);

        Assert.NotNull(toFirst);
        Assert.Equal(firstLine, toFirst!.Point.Y, 3);
        Assert.NotEqual(firstLine, thirdLine);
    }
}
