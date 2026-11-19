using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

// Drawing sets the GLOBAL drawing scale (EditorContext.RequestRedraw); these tests draw at 1:50, so they run with the other tests that change it
// and put it back to 1 when they are done.
[Collection("Scale")]
public class WallDrawTypingTests : IDisposable
{
    public void Dispose() => DrawingScale.Denominator = 1;

    private static (EditorContext Context, WallDrawTyping Typing, Shape Wall) NewWall()
    {
        var context = new EditorContext();
        context.Document.Width = 1600;
        context.Document.Height = 1600;
        context.Document.ScaleDenominator = 50;
        var wall = ShapeTemplates.NewPath(ShapeKind.Wall, 300, 300, ArgbColor.FromRgb(0x9E, 0xD9, 0xD1), DimensionKind.Aligned, LengthUnit.Centimeter, 50);
        return (context, new WallDrawTyping(context), wall);
    }

    /// <summary>The pointer (finger) is somewhere: the moving end of the wall goes there; typing then only decides how far.</summary>
    private static void PointTo(Shape wall, double dx, double dy)
    {
        var points = wall.Points!.ToList();
        var from = points[^2];
        points[^1] = new CorePoint(from.X + dx, from.Y + dy);
        wall.Points = points;
        PathBoundsCalculator.Recompute(wall);
    }

    private static void Type(WallDrawTyping typing, Shape wall, string text)
    {
        foreach (var c in text)
        {
            typing.Append(c, wall);
        }
    }

    private static double Px(double cm) => cm / 2.54 * 96 / 50;

    [Fact]
    public void TypingALength_PutsTheCornerThatFarInTheDirectionOfThePointer()
    {
        var (_, typing, wall) = NewWall();
        PointTo(wall, 120, 7); // the finger is to the right, a little down

        Type(typing, wall, "300");

        // the wall's dimension (inside length) shows 300 cm
        Assert.Equal(Px(300), WallTypedLength.Measured(wall, WallGeometry.AbsolutePoints(wall), 0), 0.6);
        // and it runs the way the finger pointed
        var points = WallGeometry.AbsolutePoints(wall);
        Assert.True(points[^1].X > points[0].X);
        Assert.Equal(7.0 / 120, (points[^1].Y - points[0].Y) / (points[^1].X - points[0].X), 3);
    }

    [Fact]
    public void Place_KeepsTheCorner_AndTheNextWallStartsThere()
    {
        var (_, typing, wall) = NewWall();
        PointTo(wall, 100, 0);
        Type(typing, wall, "250");

        Assert.Equal(TypedPlacement.Placed, typing.Place(wall));

        Assert.False(typing.IsTyping);
        Assert.Equal(3, wall.Points!.Count); // start, the placed corner, and the new moving end on top of it
        Assert.Equal(wall.Points[1], wall.Points[2]);
    }

    [Fact]
    public void BackspaceEditsTheNumber_AndAnEmptyOneGivesTheLengthBackToTheFinger()
    {
        var (_, typing, wall) = NewWall();
        PointTo(wall, 100, 0);

        Type(typing, wall, "250");
        Assert.True(typing.Backspace(wall)); // 25
        Assert.Equal(Px(25), WallTypedLength.Measured(wall, WallGeometry.AbsolutePoints(wall), 0), 0.6);

        Assert.True(typing.Backspace(wall));
        Assert.True(typing.Backspace(wall)); // nothing typed any more: the finger decides again
        Assert.False(typing.IsTyping);
        Assert.False(typing.Backspace(wall));
    }

    [Fact]
    public void ATextThatIsNoLength_PlacesNothing()
    {
        var (_, typing, wall) = NewWall();
        PointTo(wall, 100, 0);
        Type(typing, wall, "m"); // a unit without a number
        var points = wall.Points!.ToList();

        Assert.Equal(TypedPlacement.NotALength, typing.Place(wall));
        Assert.Equal(points, wall.Points);
    }

    [Fact]
    public void ARoomTypedWithFourLengths_ClosesWhenTheLastWallComesBackToTheStart()
    {
        var (_, typing, wall) = NewWall();
        var result = TypedPlacement.Placed;

        // right 400, down 300, left 400, up 300: back at the start
        (double Dx, double Dy, string Text)[] walls = [(100, 0, "400"), (0, 100, "300"), (-100, 0, "400"), (0, -100, "300")];
        foreach (var (dx, dy, text) in walls)
        {
            PointTo(wall, dx, dy);
            Type(typing, wall, text);
            result = typing.Place(wall);
        }

        Assert.Equal(TypedPlacement.RoomClosed, result);
    }

    [Fact]
    public void AWallThatEndsFree_GivesBackWhatWasAddedForACornerThatNeverCame()
    {
        var (_, typing, wall) = NewWall();
        PointTo(wall, 100, 0);
        Type(typing, wall, "400");
        typing.Place(wall);
        PointTo(wall, 0, 100);
        Type(typing, wall, "300");
        typing.Place(wall);

        // the drawing ends here (the moving end is dropped, as the canvas does): the last wall must show its typed 300
        wall.Points = wall.Points!.Take(wall.Points!.Count - 1).ToList();
        typing.FitLast(wall);

        Assert.Equal(Px(300), WallTypedLength.Measured(wall, WallGeometry.AbsolutePoints(wall), 1), 0.6);
    }

    [Fact]
    public void ForAPolyline_TheNumberIsTheLengthOfThePieceItself()
    {
        var (context, typing, _) = NewWall();
        var line = ShapeTemplates.NewPath(ShapeKind.Polyline, 300, 300, ArgbColor.FromRgb(0, 0, 0), DimensionKind.Aligned, LengthUnit.Centimeter, 50);
        PointTo(line, 100, 0);

        Type(typing, line, "10"); // 10 cm (the unit of the dimensions of the document)

        var expected = WallGeometry.TryParseLength("10", context.DimensionUnit, 50, out var px) ? px : double.NaN;
        var points = WallGeometry.AbsolutePoints(line);
        Assert.Equal(expected, Geo.Distance(points[0], points[^1]), 0.01);
    }
}

[Collection("Scale")]
public class WallDrawTypingWholeNumberTests : IDisposable
{
    public void Dispose() => DrawingScale.Denominator = 1;

    private static (EditorContext Context, WallDrawTyping Typing, Shape Wall) NewWall()
    {
        var context = new EditorContext();
        context.Document.ScaleDenominator = 40;
        var wall = ShapeTemplates.NewPath(ShapeKind.Wall, 500, 500, ArgbColor.FromRgb(0x9E, 0xD9, 0xD1), DimensionKind.Aligned, LengthUnit.Centimeter, 40);
        return (context, new WallDrawTyping(context), wall);
    }

    /// <summary>What the Length window does: the moving end is put one step in the chosen direction, the number is typed in at once, the corner is placed.</summary>
    private static void Go(WallDrawTyping typing, Shape wall, string number, double dx, double dy)
    {
        var points = wall.Points!.ToList();
        var from = points[^2];
        points[^1] = new CorePoint(from.X + dx * 100, from.Y + dy * 100);
        wall.Points = points;
        PathBoundsCalculator.Recompute(wall);
        typing.Clear();
        typing.SetTyped(number, wall);
        Assert.Equal(TypedPlacement.Placed, typing.Place(wall));
    }

    [Theory]
    [InlineData(-1, 0, 0, -1)] // left, then up: a right turn
    [InlineData(-1, 0, 0, 1)]  // left, then down: a left turn
    [InlineData(1, 0, 0, -1)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(0, -1, 1, 0)]
    [InlineData(0, 1, -1, 0)]
    public void TheSecondWall_GoesInTheDirectionThatWasChosen(double dx1, double dy1, double dx2, double dy2)
    {
        var (_, typing, wall) = NewWall();
        Go(typing, wall, "250", dx1, dy1);
        Go(typing, wall, "150", dx2, dy2);

        var points = WallGeometry.AbsolutePoints(wall);
        var corner = points[1];
        var end = points[2];
        Assert.True((end.X - corner.X) * dx2 + (end.Y - corner.Y) * dy2 > 100, $"went {end.X - corner.X:0},{end.Y - corner.Y:0} for direction {dx2},{dy2}");
        // and the first wall went in its direction too
        Assert.True((corner.X - points[0].X) * dx1 + (corner.Y - points[0].Y) * dy1 > 100);
    }

    [Fact]
    public void ARoomOfTypedWalls_HasTheTypedInsideLengths_AndClosesAtTheStart()
    {
        var (_, typing, wall) = NewWall();
        Go(typing, wall, "400", 1, 0);
        Go(typing, wall, "300", 0, 1);
        Go(typing, wall, "400", -1, 0);

        var points = WallGeometry.AbsolutePoints(wall);
        Assert.Equal(5, points.Count); // four corners and the moving end
        Assert.True(points[3].X < points[2].X, "the third wall goes left");
        Assert.True(points[2].Y > points[1].Y, "the second wall goes down");
    }
}
