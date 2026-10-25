using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class ArchitecturalSymbolTests
{
    private static Shape Symbol(ShapeKind kind, double x = 100, double y = 100, double w = 90, double h = 106, double wall = 16)
    {
        var shape = new Shape { Kind = kind, X = x, Y = y, Width = w, Height = h, WallThickness = wall };
        ArchitecturalSymbols.ApplyDefaults(shape);
        return shape;
    }

    [Fact]
    public void TheFiveSymbolKinds_AreRecognised()
    {
        Assert.All(
            new[] { ShapeKind.DoorSingle, ShapeKind.DoorDouble, ShapeKind.WindowSingle, ShapeKind.WindowDouble, ShapeKind.WallOpening },
            kind => Assert.True(ArchitecturalSymbols.IsSymbol(kind)));
        Assert.False(ArchitecturalSymbols.IsSymbol(ShapeKind.Rectangle));
        Assert.True(ArchitecturalSymbols.IsDoor(ShapeKind.DoorDouble));
        Assert.True(ArchitecturalSymbols.IsWindow(ShapeKind.WindowSingle));
    }

    [Fact]
    public void ADoor_FillsItsFrame_TheOpeningIsTheWallStripAtTheBottom()
    {
        var door = Symbol(ShapeKind.DoorSingle); // 90 wide, 106 high: 16 of wall and a 90 radius swing
        using var parts = ArchitecturalSymbols.Build(door);

        // the opening hides the wall: it spans the width and the wall strip (y = 190 .. 206) plus a hair for the outline
        Assert.Equal(100, parts.Mask.Bounds.Left, 3);
        Assert.Equal(190, parts.Mask.Bounds.Right, 3);
        Assert.InRange(parts.Mask.Bounds.Top, 188.5f, 190f);
        Assert.InRange(parts.Mask.Bounds.Bottom, 206f, 207.5f);

        using var outline = ShapeGeometryFactory.Build(door);
        Assert.Equal(100, outline.TightBounds.Left, 1);
        Assert.Equal(190, outline.TightBounds.Right, 1);
        Assert.Equal(100, outline.TightBounds.Top, 1);
        Assert.InRange(outline.TightBounds.Bottom, 206f, 207f); // the opening reaches a hair beyond the wall so its outline is hidden
    }

    [Fact]
    public void TheLeaf_StandsAtARightAngleToTheWall_AndFollowsTheOpenAngle()
    {
        var door = Symbol(ShapeKind.DoorSingle);

        using (var open = ArchitecturalSymbols.Build(door))
        {
            // hinge at the left jamb on the wall face (x = 100, y = 190), leaf straight up to the top of the frame
            Assert.InRange(open.Leaves.Bounds.Top, 99.5f, 100.5f);
            Assert.InRange(open.Leaves.Bounds.Left, 99.5f, 100.5f);
            Assert.InRange(open.Leaves.Bounds.Right, 100f, 106f); // a thin panel
        }

        door.DoorOpenAngle = 45;
        using var half = ArchitecturalSymbols.Build(door);
        Assert.InRange(half.Leaves.Bounds.Top, 190 - 90 * MathF.Sin(MathF.PI / 4) - 5f, 190 - 90 * MathF.Sin(MathF.PI / 4) + 1f);
        Assert.True(half.Leaves.Bounds.Right > 140, $"right={half.Leaves.Bounds.Right}");
    }

    [Fact]
    public void FlippingHorizontally_MovesTheHingeToTheOtherJamb_FlippingVertically_SwingsTheDoorTheOtherWay()
    {
        var door = Symbol(ShapeKind.DoorSingle);
        door.FlipHorizontal = true;
        using (var mirrored = ArchitecturalSymbols.Build(door))
        {
            Assert.InRange(mirrored.Leaves.Bounds.Right, 189f, 190.5f); // the leaf stands at the right jamb now
        }

        door.FlipHorizontal = false;
        door.FlipVertical = true;
        using var down = ArchitecturalSymbols.Build(door);
        Assert.InRange(down.Leaves.Bounds.Bottom, 205.5f, 206.5f); // the leaf swings to the bottom of the frame
        Assert.InRange(down.Mask.Bounds.Top, 99f, 101.5f);          // and the wall strip is at the top
    }

    [Fact]
    public void ADoubleDoor_HasTwoLeavesThatMeetInTheMiddle()
    {
        var door = Symbol(ShapeKind.DoorDouble, w: 160, h: 96);
        using var parts = ArchitecturalSymbols.Build(door);

        Assert.Equal(100, parts.Leaves.Bounds.Left, 1);
        Assert.InRange(parts.Leaves.Bounds.Right, 259f, 261f);
        Assert.Equal(2, ContourCount(parts.Leaves));
        // two swing arcs and the two jambs
        Assert.True(ContourCount(parts.Lines) >= 4);
    }

    [Fact]
    public void AWindow_IsTheWallStripWithGlassLines_ADoubleWindowAddsAPost()
    {
        using var single = ArchitecturalSymbols.Build(Symbol(ShapeKind.WindowSingle, w: 120, h: 16));
        using var doubleWindow = ArchitecturalSymbols.Build(Symbol(ShapeKind.WindowDouble, w: 120, h: 16));

        Assert.Equal(100, single.Mask.Bounds.Top, 3);
        Assert.Equal(116, single.Mask.Bounds.Bottom, 3);
        Assert.Equal(220, single.Mask.Bounds.Right, 3);
        Assert.True(ContourCount(doubleWindow.Lines) > ContourCount(single.Lines));
        Assert.Empty(DrawnLeaves(single));
    }

    [Fact]
    public void AnOpening_OnlyCutsTheWall_WithJambsAtBothEnds()
    {
        using var opening = ArchitecturalSymbols.Build(Symbol(ShapeKind.WallOpening, w: 100, h: 16));

        Assert.Equal(2, ContourCount(opening.Lines));
        Assert.True(opening.Leaves.IsEmpty);
        Assert.Equal(100, opening.Mask.Bounds.Width, 3);
    }

    [Fact]
    public void RotatingTurnsTheSymbolOntoAnotherWall()
    {
        var window = Symbol(ShapeKind.WindowSingle, w: 120, h: 16);
        window.Rotation = 90;

        using var parts = ArchitecturalSymbols.Build(window);

        // centred on (160, 108) and turned: 16 wide and 120 high now
        Assert.Equal(16, parts.Mask.Bounds.Width, 2);
        Assert.Equal(120, parts.Mask.Bounds.Height, 2);
        Assert.Equal(160, parts.Mask.Bounds.MidX, 2);
        Assert.Equal(108, parts.Mask.Bounds.MidY, 2);
    }

    [Fact]
    public void APlacedSymbol_CutsTheWallItLiesOver()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 20, Y = 100, WallThickness = 16, Hatch = HatchKind.Solid, BorderThickness = 2, WallShowDimensions = false,
            FillColor = ArgbColor.FromRgb(20, 20, 20), BorderColor = ArgbColor.FromRgb(0, 0, 0),
            Points = [new CorePoint(0, 0), new CorePoint(260, 0)],
        };
        PathBoundsCalculator.Recompute(wall);
        var window = Symbol(ShapeKind.WindowSingle, x: 100, y: 92, w: 100, h: 16);

        var document = new Document { Width = 300, Height = 200 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(window); // the front-most shape comes first
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);
        using var bitmap = new SKBitmap(300, 200);
        using var canvas = new SKCanvas(bitmap);
        DocumentRenderer.Render(canvas, document, 300, 200, forExport: true);

        // the wall is dark where the window is not, and light inside the opening
        Assert.True(bitmap.GetPixel(50, 100).Red < 60, "wall next to the window");
        var inside = bitmap.GetPixel(150, 95); // between the frame and the first glass line (at y = 98)
        Assert.True(inside.Red > 200, $"opening not cut: {inside}");
    }

    [Fact]
    public void ClickPlacedSizes_FollowTheRealWorldWidthAtTheDrawingScale()
    {
        var (width, height) = ArchitecturalSymbols.DefaultSize(ShapeKind.DoorSingle, 16, scaleDenominator: 50, pageWidth: 794);

        Assert.Equal(90 / 2.54 * 96 / 50, width, 3); // a 90 cm door at 1:50
        Assert.Equal(width + 16, height, 3);          // a circle of swing above the wall strip

        var (full, _) = ArchitecturalSymbols.DefaultSize(ShapeKind.DoorSingle, 16, scaleDenominator: 1, pageWidth: 794);
        Assert.Equal(794 * 0.45, full, 3);            // at 1:1 a real door would not fit the page: it is capped

        Assert.Equal(16, ArchitecturalSymbols.DefaultSize(ShapeKind.WindowSingle, 16, 50, 794).Height, 3);
        Assert.Equal(180 / 2.54 * 96 / 50, ArchitecturalSymbols.DefaultSize(ShapeKind.WindowDouble, 16, 50, 794).Width, 3);
    }

    [Fact]
    public void TheOpenAngle_SurvivesSnapshotsAndIsLimited()
    {
        var door = Symbol(ShapeKind.DoorSingle);
        door.DoorOpenAngle = 45;

        var copy = new Shape { Kind = ShapeKind.DoorSingle };
        ShapeSnapshot.Capture(door).ApplyTo(copy);

        Assert.Equal(45, copy.DoorOpenAngle);
        door.DoorOpenAngle = 500;
        Assert.Equal(120, door.DoorOpenAngle);
    }

    [Fact]
    public void SymbolsCarryNoTextLabel_AndOnlyMatchLookAspects()
    {
        Assert.False(ShapeLabel.CanHaveLabel(Symbol(ShapeKind.DoorSingle)));

        var source = Symbol(ShapeKind.DoorSingle);
        source.FillColor = ArgbColor.FromRgb(1, 2, 3);
        source.CornerRadius = 9;
        var target = Symbol(ShapeKind.WindowSingle);

        MatchProperties.Copy(source, target, MatchAspects.Look);

        Assert.Equal(source.FillColor, target.FillColor);
        Assert.Equal(0, target.CornerRadius);
    }

    private static int ContourCount(SKPath path)
    {
        var count = 0;
        using var iterator = path.CreateIterator(false);
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            if (verb == SKPathVerb.Move)
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyList<int> DrawnLeaves(ArchitecturalSymbols.Parts parts) => parts.Leaves.IsEmpty ? [] : [1];
}

public class ArchitecturalSymbolRobustnessTests
{
    [Theory]
    [InlineData(ShapeKind.DoorSingle)]
    [InlineData(ShapeKind.DoorDouble)]
    [InlineData(ShapeKind.WindowSingle)]
    [InlineData(ShapeKind.WindowDouble)]
    [InlineData(ShapeKind.WallOpening)]
    public void ASymbolBeingDraggedOut_CanBeTinyOrEmpty_WithoutFailing(ShapeKind kind)
    {
        foreach (var (w, h) in new[] { (0.0, 0.0), (1.0, 1.0), (2.0, 0.5), (300.0, 1.0), (1.0, 300.0) })
        {
            var shape = new Shape { Kind = kind, X = 10, Y = 10, Width = w, Height = h, WallThickness = 16 };
            ArchitecturalSymbols.ApplyDefaults(shape);
            shape.DoorOpenAngle = 120;

            using var parts = ArchitecturalSymbols.Build(shape);
            using var outline = ShapeGeometryFactory.Build(shape);

            Assert.False(float.IsNaN(outline.Bounds.Width));
            Assert.False(float.IsNaN(parts.Mask.Bounds.Width));
        }
    }
}

public class DoorProportionTests
{
    private static Shape Door(double width, double height, ShapeKind kind = ShapeKind.DoorSingle, double wall = 16)
    {
        var door = new Shape { Kind = kind, X = 100, Y = 100, Width = width, Height = height, WallThickness = wall };
        ArchitecturalSymbols.ApplyDefaults(door);
        return door;
    }

    [Theory]
    [InlineData(ShapeKind.DoorSingle, 90, 106)]
    [InlineData(ShapeKind.DoorSingle, 60, 76)]
    [InlineData(ShapeKind.DoorDouble, 160, 96)]
    [InlineData(ShapeKind.DoorDouble, 100, 66)]
    public void TheHeightOfADoor_FollowsFromItsWidth(ShapeKind kind, double width, double expectedHeight)
    {
        var door = Door(width, height: 5, kind);

        Assert.True(ArchitecturalSymbols.Normalize(door));

        Assert.Equal(expectedHeight, door.Height, 6);
        Assert.False(ArchitecturalSymbols.Normalize(door)); // already right
    }

    [Fact]
    public void AWindowOrOpening_KeepsTheHeightItWasGiven()
    {
        var window = Door(120, 20, ShapeKind.WindowSingle);

        Assert.False(ArchitecturalSymbols.Normalize(window));
        Assert.Equal(20, window.Height);
    }

    [Fact]
    public void TheLeafIsExactlyAsLongAsTheOpeningIsWide_AndEndsOnTheSwingArc()
    {
        var door = Door(90, 300); // whatever height the frame has
        door.DoorOpenAngle = 45;

        using var parts = ArchitecturalSymbols.Build(door);

        // the hinge is at the left jamb on the wall face (x = 100, y = 100 + 90), the tip of the leaf is 90 away from it
        var hinge = new SkiaSharp.SKPoint(100, 190);
        var tip = parts.Leaves.GetPoint(1);
        Assert.Equal(90, Geo.Distance(hinge, tip), 3);
        // the arc is a circle of that radius: all of it is 90 from the hinge
        Assert.True(parts.Lines.Bounds.Top >= 190 - 90 - 0.01f);
        Assert.True(parts.Lines.Bounds.Right <= 190.01f);
    }

    [Fact]
    public void TheDrawingDoesNotDependOnTheFrameHeight()
    {
        var right = Door(90, 106);
        var wrong = Door(90, 500);

        using var a = ArchitecturalSymbols.Build(right);
        using var b = ArchitecturalSymbols.Build(wrong);

        Assert.Equal(a.Leaves.Bounds, b.Leaves.Bounds);
        Assert.Equal(a.Mask.Bounds, b.Mask.Bounds);
        Assert.Equal(a.Lines.Bounds, b.Lines.Bounds);
    }

    [Fact]
    public void DraggingOutADoor_OnlyTheWidthCounts()
    {
        var pending = Door(0, 0);

        var frame = ShapeOutline.FrameForBox(pending, 50, 60, 80, 999);

        Assert.Equal((50, 60, 80), (frame.X, frame.Y, frame.W));
        Assert.Equal(16 + 80, frame.H, 6);
    }

    [Fact]
    public void ANewWallThickness_ChangesTheHeightToo()
    {
        var door = Door(90, 106);
        door.WallThickness = 24;

        ArchitecturalSymbols.Normalize(door);

        Assert.Equal(114, door.Height, 6);
    }
}

public class ArchitecturalSymbolHitTests
{
    private static Shape Window()
    {
        var window = new Shape { Kind = ShapeKind.WindowSingle, X = 100, Y = 100, Width = 120, Height = 16, WallThickness = 16 };
        ArchitecturalSymbols.ApplyDefaults(window);
        return window;
    }

    [Fact]
    public void ANarrowWindow_IsHitOnItAndJustBesideIt()
    {
        var window = Window(); // 100..220 x 100..116

        Assert.True(ArchitecturalSymbols.HitTest(window, 160, 108, 3));
        Assert.True(ArchitecturalSymbols.HitTest(window, 160, 119, 5)); // 3 px below it, within the 5 px of reach
        Assert.False(ArchitecturalSymbols.HitTest(window, 160, 130, 5));
        Assert.False(ArchitecturalSymbols.HitTest(window, 300, 108, 5));
    }

    [Fact]
    public void ATurnedWindow_IsHitWhereItIsDrawn_NotWhereItsUnturnedFrameWas()
    {
        var window = Window();
        window.Rotation = 90; // centred on (160, 108): now 16 wide and 120 high

        Assert.True(ArchitecturalSymbols.HitTest(window, 160, 60, 3));   // on the upper part of the turned window
        Assert.True(ArchitecturalSymbols.HitTest(window, 160, 150, 3));  // on the lower part
        Assert.False(ArchitecturalSymbols.HitTest(window, 205, 108, 3)); // where the unturned frame reached, to the right
    }

    [Fact]
    public void ADoor_IsHitOnItsLeafAndItsOpening_ButNotInTheMiddleOfTheSwing()
    {
        var door = new Shape { Kind = ShapeKind.DoorSingle, X = 100, Y = 100, Width = 90, Height = 106, WallThickness = 16 };
        ArchitecturalSymbols.ApplyDefaults(door);

        Assert.True(ArchitecturalSymbols.HitTest(door, 102, 140, 3));   // on the leaf along the hinge post
        Assert.True(ArchitecturalSymbols.HitTest(door, 145, 198, 3));   // in the opening strip
        Assert.True(ArchitecturalSymbols.HitTest(door, 180.6, 150, 3)); // on the swing arc (90 from the hinge)
        Assert.False(ArchitecturalSymbols.HitTest(door, 150, 150, 3));  // empty floor inside the swing
    }
}

[Collection("Scale")]
public class SymbolDimensionTests
{
    private static Shape Window(double w = 120, double h = 16)
    {
        var window = new Shape { Kind = ShapeKind.WindowSingle, X = 100, Y = 100, Width = w, Height = h, WallThickness = 16 };
        ArchitecturalSymbols.ApplyDefaults(window);
        return window;
    }

    [Fact]
    public void ADimensionShowsTheWidthOfTheOpening_ByDefault()
    {
        DrawingScale.Denominator = 1;
        var window = Window();

        var layout = ArchitecturalSymbols.LayoutDimension(window)!;

        Assert.Equal(DimensionRenderer.Format(120, LengthUnit.Centimeter, 2, scale: 1), layout.Text);
        Assert.True(window.SymbolShowDimension);
    }

    [Fact]
    public void AtTheDrawingScale_TheRealWidthIsWritten()
    {
        DrawingScale.Denominator = 50;
        try
        {
            var door = new Shape { Kind = ShapeKind.DoorSingle, X = 0, Y = 0, Width = 68.0315, WallThickness = 16 };
            ArchitecturalSymbols.ApplyDefaults(door);
            ArchitecturalSymbols.Normalize(door);

            Assert.Equal("90.00 cm", ArchitecturalSymbols.LayoutDimension(door)!.Text);
        }
        finally
        {
            DrawingScale.Denominator = 1;
        }
    }

    [Fact]
    public void HidingTheDimension_RemovesIt()
    {
        var window = Window();
        window.SymbolShowDimension = false;

        Assert.Null(ArchitecturalSymbols.LayoutDimension(window));
    }

    [Fact]
    public void TheDimensionIsOutsideTheWallByDefault_AndOnTheRoomSideWhenAsked()
    {
        DrawingScale.Denominator = 1;
        var window = Window(); // frame 100..220 x 100..116, the wall strip is the whole frame

        var outside = ArchitecturalSymbols.LayoutDimension(window)!;
        Assert.True(outside.TextPosition.Y > 116, $"y={outside.TextPosition.Y}");

        window.WallDimensionPlacement = WallDimensionPlacement.Inside;
        var room = ArchitecturalSymbols.LayoutDimension(window)!;
        Assert.True(room.TextPosition.Y < 100, $"y={room.TextPosition.Y}");
    }

    [Fact]
    public void FlippingTheSymbol_PutsTheOutsideOnTheOtherSide()
    {
        DrawingScale.Denominator = 1;
        var window = Window();
        window.FlipVertical = true;

        var layout = ArchitecturalSymbols.LayoutDimension(window)!;

        Assert.True(layout.TextPosition.Y < 100, $"y={layout.TextPosition.Y}");
    }

    [Fact]
    public void ADoorsDimension_SitsBelowItsWallStrip_OrAboveTheHingeFace()
    {
        DrawingScale.Denominator = 1;
        var door = new Shape { Kind = ShapeKind.DoorSingle, X = 100, Y = 100, Width = 90, WallThickness = 16 };
        ArchitecturalSymbols.ApplyDefaults(door);
        ArchitecturalSymbols.Normalize(door); // 90 x 106: the wall strip is y = 190 .. 206

        Assert.True(ArchitecturalSymbols.LayoutDimension(door)!.TextPosition.Y > 206);

        door.WallDimensionPlacement = WallDimensionPlacement.Inside;
        Assert.True(ArchitecturalSymbols.LayoutDimension(door)!.TextPosition.Y < 190);
    }

    [Fact]
    public void TheDistanceFromTheOpening_IsCustomisable()
    {
        DrawingScale.Denominator = 1;
        var window = Window();
        var near = ArchitecturalSymbols.LayoutDimension(window)!.TextPosition.Y;

        window.DimOffset = 60;
        var far = ArchitecturalSymbols.LayoutDimension(window)!.TextPosition.Y;

        Assert.True(far > near + 30, $"near={near} far={far}");
    }

    [Fact]
    public void ARotatedSymbol_CarriesItsDimensionWithIt()
    {
        DrawingScale.Denominator = 1;
        var window = Window();
        window.Rotation = 90; // centred on (160, 108): 16 wide and 120 high, the outside is to the left now

        var layout = ArchitecturalSymbols.LayoutDimension(window)!;

        Assert.True(layout.TextPosition.X < 152, $"x={layout.TextPosition.X}");
        Assert.InRange(layout.TextPosition.Y, 90f, 126f);
    }

    [Fact]
    public void TheFlagSurvivesSnapshots()
    {
        var window = Window();
        window.SymbolShowDimension = false;

        var copy = new Shape { Kind = ShapeKind.WindowSingle };
        ShapeSnapshot.Capture(window).ApplyTo(copy);

        Assert.False(copy.SymbolShowDimension);
    }
}

[Collection("Scale")]
public class SymbolMeasurementTests
{
    /// <summary>A room whose wall centre lines run 100..500 x 100..400, 16 px thick (inner faces at 108 / 492 and 108 / 392), with a window in
    /// the top wall from x = 150 to 230.</summary>
    private static (Document Document, Shape Wall, Shape Window) RoomWithWindow()
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
        return (document, wall, window);
    }

    private static double Gap(Document document, Shape symbol, SymbolDimensionPart part) =>
        SymbolMeasurements.Layout(document, symbol).Single(d => d.Part == part).Length;

    [Fact]
    public void ASymbolFindsItsWall_AndMeasuresTheClearDistancesToTheCorners()
    {
        var (document, wall, window) = RoomWithWindow();

        var host = SymbolMeasurements.FindHost(document, window)!;
        Assert.Same(wall, host.Wall);
        Assert.Equal(0, host.Segment);

        // the inner face of the top wall runs from x = 108 to 492
        Assert.Equal(42, Gap(document, window, SymbolDimensionPart.GapToStart), 3);  // 150 - 108
        Assert.Equal(262, Gap(document, window, SymbolDimensionPart.GapToEnd), 3);   // 492 - 230
        Assert.Equal(80, Gap(document, window, SymbolDimensionPart.Width), 3);
    }

    [Fact]
    public void TheDistancesAreMeasuredOnTheInnerFace_NotOnTheCentreLine()
    {
        var (document, _, window) = RoomWithWindow();

        // measured from the centre line it would be 50 (150 - 100): the wall corner is 8 px further in
        Assert.NotEqual(50, Gap(document, window, SymbolDimensionPart.GapToStart), 3);
    }

    [Fact]
    public void ASymbolInNoWall_HasOnlyItsWidth()
    {
        var (document, _, window) = RoomWithWindow();
        window.Y = 250; // in the middle of the room

        var dimensions = SymbolMeasurements.Layout(document, window);

        Assert.Single(dimensions);
        Assert.Equal(SymbolDimensionPart.Width, dimensions[0].Part);
    }

    [Fact]
    public void TheDistancesCanBeHidden_TheWidthStays()
    {
        var (document, _, window) = RoomWithWindow();
        window.SymbolShowGaps = false;

        var dimensions = SymbolMeasurements.Layout(document, window);

        Assert.Single(dimensions);
        Assert.Equal(SymbolDimensionPart.Width, dimensions[0].Part);
    }

    [Fact]
    public void TypingADistanceToTheStartCorner_SlidesTheWindowAlongTheWall()
    {
        var (document, _, window) = RoomWithWindow();

        Assert.True(SymbolMeasurements.SetGap(document, window, SymbolDimensionPart.GapToStart, 100));

        Assert.Equal(100, Gap(document, window, SymbolDimensionPart.GapToStart), 3);
        Assert.Equal(208, window.X, 3);                      // 108 + 100
        Assert.Equal(92, window.Y, 3);                       // still in the wall
        Assert.Equal(204, Gap(document, window, SymbolDimensionPart.GapToEnd), 3); // 492 - 288
    }

    [Fact]
    public void TypingADistanceToTheEndCorner_SlidesItTheOtherWay()
    {
        var (document, _, window) = RoomWithWindow();

        Assert.True(SymbolMeasurements.SetGap(document, window, SymbolDimensionPart.GapToEnd, 100));

        Assert.Equal(100, Gap(document, window, SymbolDimensionPart.GapToEnd), 3);
        Assert.Equal(312, window.X, 3); // the right jamb at 492 - 100 = 392 = X + 80
    }

    [Fact]
    public void AWindowInAnotherWall_IsMeasuredAlongThatWall()
    {
        var (document, _, window) = RoomWithWindow();
        window.Rotation = 90;
        window.X = 100 - 32; // centred on the left wall (x = 100): frame centre (100, 108 + ...) set below
        window.Y = 200;
        // frame 80 x 16 turned by 90 degrees about its centre (X + 40, Y + 8) = (108, 208): it spans y = 168 .. 248 at x = 100..116
        window.X = 60;

        var gapStart = Gap(document, window, SymbolDimensionPart.GapToStart);

        // the left wall runs from the bottom up (segment 3: (100,400) -> (100,100)); its inner face from y = 392 to 108
        Assert.InRange(gapStart, 130, 230);
        Assert.Contains(SymbolMeasurements.Layout(document, window), d => d.Part == SymbolDimensionPart.GapToEnd);
    }

    [Fact]
    public void TypingAWidth_KeepsTheStartJambWhereItIs()
    {
        var (_, _, window) = RoomWithWindow();

        Assert.True(SymbolMeasurements.SetOpeningWidth(window, 120));

        Assert.Equal(120, window.Width);
        Assert.Equal(150, window.X, 3);
        Assert.Equal(92, window.Y, 3);
    }

    [Fact]
    public void TypingAWidthForARotatedDoor_KeepsItsStartJambToo()
    {
        DrawingScale.Denominator = 1;
        var door = new Shape { Kind = ShapeKind.DoorSingle, X = 100, Y = 100, Width = 90, WallThickness = 16, Rotation = 90 };
        ArchitecturalSymbols.ApplyDefaults(door);
        ArchitecturalSymbols.Normalize(door);

        SKPoint Jamb(Shape s)
        {
            var (w, h) = ArchitecturalSymbols.FrameSize(s);
            return ArchitecturalSymbols.PlaceMatrix(s, w, h).MapPoint(new SKPoint(0, h - 8));
        }

        var before = Jamb(door);
        SymbolMeasurements.SetOpeningWidth(door, 60);
        var after = Jamb(door);

        Assert.Equal(before.X, after.X, 2);
        Assert.Equal(before.Y, after.Y, 2);
        Assert.Equal(76, door.Height, 3); // 16 + 60
    }

    [Fact]
    public void AFlatDocumentDrawsTheDistancesNextToTheWindow()
    {
        var (document, _, window) = RoomWithWindow();
        using var bitmap = new SKBitmap(700, 500);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        DocumentRenderer.Render(canvas, document, 700, 500, forExport: true);

        // red dimension pixels exist below the top wall, left of the window (the distance to the start corner)
        var red = false;
        for (var x = 112; x < 148 && !red; x++)
        {
            for (var y = 108; y < 140 && !red; y++)
            {
                var c = bitmap.GetPixel(x, y);
                red = c.Red > 150 && c.Green < 90 && c.Blue < 90;
            }
        }

        Assert.True(red, "the distance to the corner is not drawn");
    }
}
