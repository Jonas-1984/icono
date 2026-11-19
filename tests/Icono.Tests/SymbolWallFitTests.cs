using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class SymbolWallFitTests
{
    /// <summary>A closed room of 400 x 300 with its corner at (100, 100) and walls 16 thick (centre lines).</summary>
    private static (Document Document, Shape Wall) Room()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16, WallClosed = true,
            Points = [new CorePoint(0, 0), new CorePoint(400, 0), new CorePoint(400, 300), new CorePoint(0, 300)],
        };
        PathBoundsCalculator.Recompute(wall);
        var document = new Document { Width = 794, Height = 1123 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);
        return (document, wall);
    }

    private static Shape Symbol(ShapeKind kind, double width = 60, double thickness = 5)
    {
        var symbol = new Shape { Kind = kind, Width = width, WallThickness = thickness, X = 0, Y = 0 };
        symbol.Height = ArchitecturalSymbols.IsDoor(kind) ? ArchitecturalSymbols.DoorHeight(symbol) : thickness;
        return symbol;
    }

    private static SKPoint CentreOf(Shape symbol) => new((float)(symbol.X + symbol.Width / 2), (float)(symbol.Y + symbol.Height / 2));

    /// <summary>Where the middle of the wall strip of a door (the bottom of its frame) or the middle of a window ends up on the page.</summary>
    private static SKPoint StripCentre(Shape symbol)
    {
        var (w, h) = ArchitecturalSymbols.FrameSize(symbol);
        var matrix = ArchitecturalSymbols.PlaceMatrix(symbol, w, h);
        return matrix.MapPoint(new SKPoint(w / 2, h - (float)symbol.WallThickness / 2));
    }

    /// <summary>The direction the door swings in, as seen on the page.</summary>
    private static SKPoint Swing(Shape symbol)
    {
        var (w, h) = ArchitecturalSymbols.FrameSize(symbol);
        var v = ArchitecturalSymbols.PlaceMatrix(symbol, w, h).MapVector(0, -1);
        return v;
    }

    [Fact]
    public void AWindowOnTheTopWall_SitsInIt_AsThickAsTheWall()
    {
        var (document, _) = Room();
        var window = Symbol(ShapeKind.WindowSingle, 100);

        Assert.True(SymbolWallFit.Fit(document, window, 250, 103, 10));

        Assert.True(window.Rotation % 180 < 0.001, $"rotation {window.Rotation}"); // along the wall (0 or 180: the turn keeps the room side, so "inside" dimensions go to the room)
        Assert.Equal(16, window.WallThickness, 3);
        Assert.Equal(16, window.Height, 3);
        Assert.Equal(250, CentreOf(window).X, 1);
        Assert.Equal(100, CentreOf(window).Y, 1); // on the centre line, though the tap was 3 beside it
    }

    [Fact]
    public void AWindowOnAnUprightWall_TurnsToIt()
    {
        var (document, _) = Room();
        var window = Symbol(ShapeKind.WindowSingle, 100);

        Assert.True(SymbolWallFit.Fit(document, window, 100, 250, 10)); // the left wall

        Assert.True(Math.Abs(((window.Rotation + 360) % 180) - 90) < 0.01, $"rotation {window.Rotation}");
        var centre = CentreOf(window);
        Assert.Equal(100, centre.X, 1);
        Assert.Equal(250, centre.Y, 1);
    }

    [Theory]
    [InlineData(250, 100, 0, 1)]     // the top wall: the room is below
    [InlineData(250, 400, 0, -1)]    // the bottom wall: the room is above
    [InlineData(100, 250, 1, 0)]     // the left wall: the room is to the right
    [InlineData(500, 250, -1, 0)]    // the right wall: the room is to the left
    public void ADoor_SwingsIntoTheRoom_OnEveryWall(double x, double y, double swingX, double swingY)
    {
        var (document, _) = Room();
        var door = Symbol(ShapeKind.DoorSingle, 90);

        Assert.True(SymbolWallFit.Fit(document, door, x, y, 10));

        var swing = Swing(door);
        Assert.Equal(swingX, swing.X, 3);
        Assert.Equal(swingY, swing.Y, 3);
        // the wall strip of the door is on the wall
        var strip = StripCentre(door);
        if (swingX == 0)
        {
            Assert.Equal(y, strip.Y, 1);
        }
        else
        {
            Assert.Equal(x, strip.X, 1);
        }
    }

    [Fact]
    public void ADoorNearACorner_StaysInsideTheSideOfTheWall()
    {
        var (document, _) = Room();
        var door = Symbol(ShapeKind.DoorSingle, 90);

        SymbolWallFit.Fit(document, door, 104, 100, 10); // the very corner of the top wall

        Assert.True(StripCentre(door).X >= 100 + 45 - 0.5, $"x {StripCentre(door).X}");
    }

    [Fact]
    public void OnASlantedWall_TheSymbolTurnsToItsAngle()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 10,
            Points = [new CorePoint(0, 0), new CorePoint(200, 200)],
        };
        PathBoundsCalculator.Recompute(wall);
        var document = new Document { Width = 794, Height = 1123 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);
        var window = Symbol(ShapeKind.WindowSingle, 60);

        Assert.True(SymbolWallFit.Fit(document, window, 200, 200, 10));

        Assert.True(Math.Abs(((window.Rotation + 360) % 180) - 45) < 0.01, $"rotation {window.Rotation}");
    }

    [Fact]
    public void AwayFromEveryWall_TheSymbolIsLeftAlone()
    {
        var (document, _) = Room();
        var door = Symbol(ShapeKind.DoorSingle, 90);
        door.X = 33;
        door.Y = 44;

        Assert.False(SymbolWallFit.Fit(document, door, 300, 250, 10)); // the middle of the room

        Assert.Equal(33, door.X);
        Assert.Equal(44, door.Y);
        Assert.Equal(0, door.Rotation);
    }

    [Fact]
    public void AWallOnAHiddenLayer_IsNotUsed()
    {
        var (document, _) = Room();
        document.Layers[0].Visible = false;

        Assert.False(SymbolWallFit.Fit(document, Symbol(ShapeKind.WindowSingle), 250, 100, 10));
    }

    [Fact]
    public void OnAnOpenWall_TheDoorSwingsToTheSideThatWasTapped()
    {
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 10,
            Points = [new CorePoint(0, 0), new CorePoint(300, 0)],
        };
        PathBoundsCalculator.Recompute(wall);
        var document = new Document { Width = 794, Height = 1123 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(wall);
        document.Layers.Add(layer);

        var below = Symbol(ShapeKind.DoorSingle, 80);
        SymbolWallFit.Fit(document, below, 250, 104, 10);
        var above = Symbol(ShapeKind.DoorSingle, 80);
        SymbolWallFit.Fit(document, above, 250, 96, 10);

        Assert.True(Swing(below).Y > 0.9);
        Assert.True(Swing(above).Y < -0.9);
    }

    [Fact]
    public void ADraggedOpening_GetsTheWidthOfTheDrag()
    {
        var (document, _) = Room();
        var window = Symbol(ShapeKind.WindowSingle, 10);

        Assert.True(SymbolWallFit.Fit(document, window, 250, 100, 10, width: 130));

        Assert.Equal(130, window.Width, 3);
    }
}
