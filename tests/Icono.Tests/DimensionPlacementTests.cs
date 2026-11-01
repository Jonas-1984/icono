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
