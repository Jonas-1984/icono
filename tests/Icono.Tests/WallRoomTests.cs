using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

[Collection("Scale")]
public class WallRoomTests
{
    private static List<(double X, double Y)> Abs(Shape wall) => wall.Points!.Select(p => (wall.X + p.X, wall.Y + p.Y)).ToList();

    private static double Length((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    [Theory]
    [InlineData(RoomPreset.Closed, 4)]
    [InlineData(RoomPreset.OneCorner, 6)]
    [InlineData(RoomPreset.OneSlantedWall, 5)]
    [InlineData(RoomPreset.TwoSlantedWalls, 6)]
    [InlineData(RoomPreset.CornerColumn, 6)]
    [InlineData(RoomPreset.WallColumn, 8)]
    public void EveryPreset_IsAClosedWallFillingItsFrame(RoomPreset preset, int corners)
    {
        var wall = WallRooms.Build(preset, 400, 300, 200, 100);

        Assert.Equal(ShapeKind.Wall, wall.Kind);
        Assert.True(wall.WallClosed);
        Assert.Equal(corners, wall.Points!.Count);

        var points = Abs(wall);
        Assert.Equal(300, points.Min(p => p.X), 6);
        Assert.Equal(500, points.Max(p => p.X), 6);
        Assert.Equal(250, points.Min(p => p.Y), 6);
        Assert.Equal(350, points.Max(p => p.Y), 6);
    }

    [Fact]
    public void TheSlantedPresets_HaveSlantedWalls_AndTheColumnsHaveRightAnglesOnly()
    {
        static int Slanted(List<(double X, double Y)> p) => Enumerable.Range(0, p.Count)
            .Count(i => Math.Abs(p[i].X - p[(i + 1) % p.Count].X) > 1e-6 && Math.Abs(p[i].Y - p[(i + 1) % p.Count].Y) > 1e-6);

        Assert.Equal(0, Slanted(Abs(WallRooms.Build(RoomPreset.Closed, 0, 0, 100, 80))));
        Assert.Equal(1, Slanted(Abs(WallRooms.Build(RoomPreset.OneSlantedWall, 0, 0, 100, 80))));
        Assert.Equal(2, Slanted(Abs(WallRooms.Build(RoomPreset.TwoSlantedWalls, 0, 0, 100, 80))));
        Assert.Equal(0, Slanted(Abs(WallRooms.Build(RoomPreset.CornerColumn, 0, 0, 100, 80))));
        Assert.Equal(0, Slanted(Abs(WallRooms.Build(RoomPreset.WallColumn, 0, 0, 100, 80))));
    }

    [Fact]
    public void ChangingOneSideOfARectangle_KeepsItRectangular()
    {
        var wall = WallRooms.Build(RoomPreset.Closed, 200, 150, 200, 100); // 100,100 .. 300,200

        var moves = WallEditor.ResizeSegment(wall, 0, 260); // the top side, 200 -> 260

        Assert.NotEmpty(moves);
        var p = Abs(wall);
        Assert.Equal(260, Length(p[0], p[1]), 6);
        Assert.Equal(100, Length(p[1], p[2]), 6); // the sides stay as long as they were
        Assert.Equal(260, Length(p[2], p[3]), 6);
        Assert.Equal(100, Length(p[3], p[0]), 6);
        Assert.Equal(p[1].Y, p[0].Y, 6);
        Assert.Equal(p[2].X, p[1].X, 6);
    }

    [Fact]
    public void ChangingTheClosingSide_MovesItsEndPointAndTheWallThatSlidesWithIt()
    {
        var wall = WallRooms.Build(RoomPreset.Closed, 200, 150, 200, 100);

        WallEditor.ResizeSegment(wall, 3, 140); // the left side runs from the bottom left up to the top left, 100 -> 140

        var p = Abs(wall);
        Assert.Equal(140, Length(p[3], p[0]), 6);
        Assert.Equal(200, Length(p[0], p[1]), 6); // the top side slides, it keeps its length
        Assert.Equal(p[1].X, p[2].X, 6);
    }

    [Fact]
    public void AnOpenWall_MovesOnlyItsEnd_AndASlantedNeighbourOnlyChangesItsAngle()
    {
        var wall = WallRooms.Build(RoomPreset.OneSlantedWall, 200, 150, 200, 100);
        wall.WallClosed = false;
        var before = Abs(wall);

        WallEditor.ResizeSegment(wall, 0, 150); // the straight top side (corner 0 to corner 1, 136 long)

        var after = Abs(wall);
        Assert.Equal(150, Length(after[0], after[1]), 6);
        Assert.Equal(before[0], after[0]);
        Assert.Equal(before[3], after[3]); // far corners stay
        Assert.NotEqual(Length(before[1], before[2]), Length(after[1], after[2]), 3); // the slanted wall changed
    }

    [Fact]
    public void TooShortOrUnknownSegments_ChangeNothing()
    {
        var wall = WallRooms.Build(RoomPreset.Closed, 200, 150, 200, 100);
        var before = Abs(wall);

        Assert.Empty(WallEditor.ResizeSegment(wall, 0, 0.1));
        Assert.Empty(WallEditor.ResizeSegment(wall, 9, 50));
        Assert.Empty(WallEditor.ResizeSegment(wall, 0, 200)); // the length it already has
        Assert.Equal(before, Abs(wall));
    }

    [Fact]
    public void WallsJoinedToAMovedCorner_FollowIt()
    {
        var room = WallRooms.Build(RoomPreset.Closed, 200, 150, 200, 100); // top left 100,100
        var corner = Abs(room)[1]; // 300,100
        var other = new Shape
        {
            Kind = ShapeKind.Wall, X = corner.X, Y = corner.Y, Points = [new CorePoint(0, 0), new CorePoint(0, -60)],
        };
        PathBoundsCalculator.Recompute(other);

        var moves = WallEditor.ResizeSegment(room, 0, 260);
        Assert.True(WallEditor.FollowMoves(other, moves));

        Assert.Equal(360, Abs(other)[0].X, 6); // its start was on the moved corner
        Assert.Equal(100, Abs(other)[0].Y, 6);
    }

    [Theory]
    [InlineData("250", LengthUnit.Centimeter, 1, 250 / 2.54 * 96)]
    [InlineData("2,5 m", LengthUnit.Centimeter, 1, 250 / 2.54 * 96)]
    [InlineData("2500mm", LengthUnit.Meter, 1, 250 / 2.54 * 96)]
    [InlineData("98.4252 in", LengthUnit.Centimeter, 1, 250 / 2.54 * 96)]
    [InlineData("250", LengthUnit.Centimeter, 50, 250 / 2.54 * 96 / 50)]
    public void TypedLengths_AreReadInAnyUnit_AtTheDrawingScale(string text, LengthUnit unit, double scale, double expectedPx)
    {
        Assert.True(WallGeometry.TryParseLength(text, unit, scale, out var px));
        Assert.Equal(expectedPx, px, 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("12 parsecs")]
    public void NonsenseIsNotALength(string text)
    {
        Assert.False(WallGeometry.TryParseLength(text, LengthUnit.Centimeter, 1, out _));
    }

    [Fact]
    public void WhatTheDimensionWrites_CanBeTypedBackExactly()
    {
        var written = DimensionRenderer.Format(333.3, LengthUnit.Centimeter, 2, scale: 20);

        Assert.True(WallGeometry.TryParseLength(written, LengthUnit.Centimeter, 20, out var px));
        Assert.Equal(333.3, px, 1);
    }

    [Fact]
    public void ClickingTheNumberOfADimension_FindsItsSegment()
    {
        var wall = WallRooms.Build(RoomPreset.Closed, 200, 150, 200, 100);
        wall.DimOffset = 30;

        var layout = WallRenderer.LayoutSegmentDimension(wall, 0)!;
        var hit = WallRenderer.HitTestDimensionText(wall, layout.TextPosition.X, layout.TextPosition.Y, 2);

        Assert.Equal(0, hit);
        Assert.Null(WallRenderer.HitTestDimensionText(wall, 200, 150, 2)); // the middle of the room
        wall.WallShowDimensions = false;
        Assert.Null(WallRenderer.HitTestDimensionText(wall, layout.TextPosition.X, layout.TextPosition.Y, 2));
    }
}

[Collection("Scale")]
public class WallDimensionPlacementTests
{
    /// <summary>A room 400 x 300 px whose top wall has a narrow pocket (a small column) of <paramref name="pocketWidth"/> x 24 px.</summary>
    private static Shape RoomWithPocket(double pocketWidth)
    {
        var left = 100.0;
        var top = 100.0;
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = left, Y = top, WallClosed = true, WallThickness = 12, DimOffset = 14,
            Points =
            [
                new CorePoint(0, 0), new CorePoint(200 - pocketWidth / 2, 0), new CorePoint(200 - pocketWidth / 2, 24),
                new CorePoint(200 + pocketWidth / 2, 24), new CorePoint(200 + pocketWidth / 2, 0), new CorePoint(400, 0),
                new CorePoint(400, 300), new CorePoint(0, 300),
            ],
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static (float X, float Y)[] Corners(DimensionLayout l)
    {
        var half = l.TextWidth / 2;
        var rad = l.TextAngle * Math.PI / 180;
        return new[] { (-half, -5f), (half, -5f), (half, 5f), (-half, 5f) }
            .Select(c => ((float)(l.TextPosition.X + c.Item1 * Math.Cos(rad) - c.Item2 * Math.Sin(rad)),
                          (float)(l.TextPosition.Y + c.Item1 * Math.Sin(rad) + c.Item2 * Math.Cos(rad)))).ToArray();
    }

    [Fact]
    public void ANumberInATightPocket_MovesToTheOutsideOfThePlan()
    {
        DrawingScale.Denominator = 1;
        var wall = RoomWithPocket(30);
        wall.WallDimensionMeasure = WallMeasure.Centre; // the centre-line dimensions sit on the outer side, where the pocket is tight

        var floor = WallRenderer.LayoutSegmentDimension(wall, 2)!; // the 30 px wide floor of the pocket

        // the top wall's outer face is at y = 94: the number now sits above it, in line with the dimensions of the top wall
        Assert.True(floor.TextPosition.Y < 94, $"number at y={floor.TextPosition.Y}");
        var topWall = WallRenderer.LayoutSegmentDimension(wall, 0)!;
        Assert.InRange(floor.TextPosition.Y, topWall.TextPosition.Y - 15, topWall.TextPosition.Y + 1); // on the same line (its number is lifted a little off it)
    }

    [Fact]
    public void NoTwoNumbersOfAWallOverlap()
    {
        DrawingScale.Denominator = 1;
        var wall = RoomWithPocket(30);

        var layouts = Enumerable.Range(0, wall.Points!.Count).Select(i => WallRenderer.LayoutSegmentDimension(wall, i)).Where(l => l is not null).ToList();
        for (var i = 0; i < layouts.Count; i++)
        {
            for (var j = i + 1; j < layouts.Count; j++)
            {
                var a = Corners(layouts[i]!);
                var b = Corners(layouts[j]!);
                var overlap = a.Min(p => p.X) < b.Max(p => p.X) && b.Min(p => p.X) < a.Max(p => p.X) &&
                              a.Min(p => p.Y) < b.Max(p => p.Y) && b.Min(p => p.Y) < a.Max(p => p.Y);
                Assert.False(overlap, $"numbers {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void RoomyDimensions_StayWhereTheyAre()
    {
        DrawingScale.Denominator = 1;
        var wall = RoomWithPocket(120); // a pocket with room for its numbers

        var floor = WallRenderer.LayoutSegmentDimension(wall, 2)!;

        Assert.True(floor.TextPosition.Y > 94, "the number of a roomy pocket stays next to it");
    }

    [Fact]
    public void ADimensionWithItsOwnOffset_IsNotMoved()
    {
        DrawingScale.Denominator = 1;
        var wall = RoomWithPocket(30);
        var inside = WallRenderer.LayoutSegmentDimension(wall, 2)!.TextPosition;
        wall.WallDimensionOverrides = [new WallDimensionOverride(2, Offset: 14)];

        var kept = WallRenderer.LayoutSegmentDimension(wall, 2)!;

        Assert.True(kept.TextPosition.Y > 88, $"a dimension the user placed stays where it was put (y={kept.TextPosition.Y})");
        Assert.NotEqual(inside.Y, kept.TextPosition.Y);
    }
}

[Collection("Scale")]
public class WallMeasureTests
{
    private static Shape Room(double thickness = 16) => new()
    {
        Kind = ShapeKind.Wall, X = 100, Y = 100, WallClosed = true, WallThickness = thickness, WallShowDimensions = true,
        Points = [new CorePoint(0, 0), new CorePoint(400, 0), new CorePoint(400, 300), new CorePoint(0, 300)],
    };

    [Fact]
    public void ByDefaultADimensionShowsTheInsideOfTheRoom_NotTheCentreLine()
    {
        var room = Room();

        Assert.Equal(WallMeasure.Inside, room.WallDimensionMeasure);
        Assert.Equal(384, WallGeometry.MeasuredLength(room, 0), 3); // 400 - 16
        Assert.Equal(284, WallGeometry.MeasuredLength(room, 1), 3); // 300 - 16
        Assert.Equal(384, WallGeometry.MeasuredLength(room, 2), 3);
        Assert.Equal(284, WallGeometry.MeasuredLength(room, 3), 3);
    }

    [Fact]
    public void TheOtherMeasures_AreTheCentreLineAndTheOutside()
    {
        var room = Room();

        room.WallDimensionMeasure = WallMeasure.Centre;
        Assert.Equal(400, WallGeometry.MeasuredLength(room, 0), 3);

        room.WallDimensionMeasure = WallMeasure.Outside;
        Assert.Equal(416, WallGeometry.MeasuredLength(room, 0), 3); // 400 + 16
        Assert.Equal(316, WallGeometry.MeasuredLength(room, 1), 3);
    }

    [Fact]
    public void ThePageDimensionWritesTheInsideLength_AtTheDrawingScale()
    {
        DrawingScale.Denominator = 1;
        var room = Room();
        var layout = WallRenderer.LayoutSegmentDimension(room, 0)!;

        Assert.Equal(DimensionRenderer.Format(384, LengthUnit.Centimeter, 2, scale: 1), layout.Text);

        room.WallDimensionMeasure = WallMeasure.Centre;
        Assert.Equal(DimensionRenderer.Format(400, LengthUnit.Centimeter, 2, scale: 1), WallRenderer.LayoutSegmentDimension(room, 0)!.Text);
    }

    [Fact]
    public void TheInsideDimensionsSitInTheRoom_TheOutsideOnesOutside()
    {
        DrawingScale.Denominator = 1;
        var room = Room();

        var inside = WallRenderer.LayoutSegmentDimension(room, 0)!; // the top wall, its inside face is at y = 108
        Assert.True(inside.TextPosition.Y > 108, $"y={inside.TextPosition.Y}");

        room.WallDimensionMeasure = WallMeasure.Outside;
        var outside = WallRenderer.LayoutSegmentDimension(room, 0)!;
        Assert.True(outside.TextPosition.Y < 92, $"y={outside.TextPosition.Y}");
    }

    [Fact]
    public void AnInnerCornerAddsLength_AnOuterCornerTakesItAway()
    {
        // an L-shaped room: the corner at (200,150) points into the room (a reflex corner seen from inside)
        var room = new Shape
        {
            Kind = ShapeKind.Wall, X = 0, Y = 0, WallClosed = true, WallThickness = 20, WallShowDimensions = true,
            Points = [new CorePoint(0, 0), new CorePoint(300, 0), new CorePoint(300, 150), new CorePoint(150, 150), new CorePoint(150, 300), new CorePoint(0, 300)],
        };

        // segment 2 runs from (300,150) to (150,150): its inside face starts at the room's wall (300 - 10) and ends where the
        // reflex corner lets it run 10 further (150 - 10 is the other way round: the corner at (150,150) turns away from the room)
        var inside = WallGeometry.MeasuredLength(room, 2);

        Assert.Equal(150 - 10 + 10, inside, 3); // 150 centre line, -10 at the convex corner, +10 at the reflex corner
    }

    [Fact]
    public void AStraightOpenWall_MeasuresTheSameOnEveryMeasure()
    {
        var wall = new Shape { Kind = ShapeKind.Wall, X = 50, Y = 50, WallThickness = 14, Points = [new CorePoint(0, 0), new CorePoint(200, 0)] };

        foreach (var measure in Enum.GetValues<WallMeasure>())
        {
            wall.WallDimensionMeasure = measure;
            Assert.Equal(200, WallGeometry.MeasuredLength(wall, 0), 3);
        }
    }
}

[Collection("Scale")]
public class WallDimensionPlacementChoiceTests
{
    private static Shape Room() => new()
    {
        Kind = ShapeKind.Wall, X = 100, Y = 100, WallClosed = true, WallThickness = 16, WallShowDimensions = true, DimOffset = 14,
        Points = [new CorePoint(0, 0), new CorePoint(400, 0), new CorePoint(400, 300), new CorePoint(0, 300)],
    };

    [Fact]
    public void Placement_DecidesWhetherTheLinesRunInsideOrOutsideThePlan_WhateverIsMeasured()
    {
        DrawingScale.Denominator = 1;
        var room = Room(); // measures the inside by default

        room.WallDimensionPlacement = WallDimensionPlacement.Inside;
        var inside = WallRenderer.LayoutSegmentDimension(room, 0)!; // the top wall: outer face y = 92, inner face y = 108
        Assert.True(inside.TextPosition.Y > 108, $"y={inside.TextPosition.Y}");

        room.WallDimensionPlacement = WallDimensionPlacement.Outside;
        var outside = WallRenderer.LayoutSegmentDimension(room, 0)!;
        Assert.True(outside.TextPosition.Y < 92, $"y={outside.TextPosition.Y}");

        // the number is the same either way: it is what is measured that decides it
        Assert.Equal(inside.Text, outside.Text);
        Assert.Equal(DimensionRenderer.Format(384, LengthUnit.Centimeter, 2, scale: 1), outside.Text);
    }

    [Fact]
    public void AnInsideMeasureOutsideThePlan_HasExtensionLinesThatReachTheInnerCorners()
    {
        DrawingScale.Denominator = 1;
        var room = Room();
        room.WallDimensionPlacement = WallDimensionPlacement.Outside;

        var layout = WallRenderer.LayoutSegmentDimension(room, 0)!;

        // the dimension line is above the wall; the lines reaching down to the inner corners (x = 108 and 492 at y = 108) are in the path
        var bounds = layout.Lines.Bounds;
        Assert.True(bounds.Top < 92);
        Assert.True(bounds.Bottom > 98, $"bottom={bounds.Bottom}"); // reaches through the wall (92..108) to its inner face
        Assert.InRange(bounds.Left, 100, 112);
        Assert.InRange(bounds.Right, 488, 500);
    }

    [Fact]
    public void AnOutsideMeasureInsideThePlan_ShowsTheOuterLength()
    {
        DrawingScale.Denominator = 1;
        var room = Room();
        room.WallDimensionMeasure = WallMeasure.Outside;
        room.WallDimensionPlacement = WallDimensionPlacement.Inside;

        var layout = WallRenderer.LayoutSegmentDimension(room, 0)!;

        Assert.Equal(DimensionRenderer.Format(416, LengthUnit.Centimeter, 2, scale: 1), layout.Text);
        Assert.True(layout.TextPosition.Y > 108);
    }

    [Fact]
    public void ASingleDimensionCanStillGoToTheOtherSide()
    {
        DrawingScale.Denominator = 1;
        var room = Room();
        room.WallDimensionPlacement = WallDimensionPlacement.Inside;
        room.WallDimensionOverrides = [new WallDimensionOverride(0, Flip: true)];

        var flipped = WallRenderer.LayoutSegmentDimension(room, 0)!;
        var normal = WallRenderer.LayoutSegmentDimension(room, 2)!;

        Assert.True(flipped.TextPosition.Y < 92);
        Assert.True(normal.TextPosition.Y < 392, $"y={normal.TextPosition.Y}"); // the bottom wall dimension stays in the room, above its inner face (y = 392)
    }

    [Fact]
    public void ExplicitPlacement_IsNeverMovedByTheAutomaticRelocation()
    {
        DrawingScale.Denominator = 1;
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallClosed = true, WallThickness = 12, DimOffset = 14, WallDimensionMeasure = WallMeasure.Centre,
            Points = [new CorePoint(0, 0), new CorePoint(185, 0), new CorePoint(185, 24), new CorePoint(215, 24), new CorePoint(215, 0), new CorePoint(400, 0), new CorePoint(400, 300), new CorePoint(0, 300)],
        };

        var automatic = WallRenderer.LayoutSegmentDimension(wall, 2)!;
        wall.WallDimensionPlacement = WallDimensionPlacement.Outside;
        var explicitOutside = WallRenderer.LayoutSegmentDimension(wall, 2)!;

        Assert.True(automatic.TextPosition.Y < 85, "automatic moves the tight dimension out of the plan");
        Assert.True(explicitOutside.TextPosition.Y > 88, $"an explicit choice keeps the dimension next to its wall (y={explicitOutside.TextPosition.Y})");
    }
}
