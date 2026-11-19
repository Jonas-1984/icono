using Icono.Rendering;

namespace Icono.Tests;

public class LiveInkTests
{
    private static float[] Ramp(int count, float from, float to) => Enumerable.Range(0, count).Select(i => from + (to - from) * i / (count - 1)).ToArray();

    [Fact]
    public void WithoutPressure_TheWholeStrokeIsOnePieceOfThePenWidth()
    {
        var plan = FreehandBuilder.LivePlan([], 500, 3, 0.7, usePressure: false);

        var piece = Assert.Single(plan);
        Assert.Equal(0, piece.From);
        Assert.Equal(499, piece.To);
        Assert.Equal(3f, piece.Width);
    }

    [Fact]
    public void AnEvenPressure_IsOnePiece_AtHalfPressureThePenWidth()
    {
        var plan = FreehandBuilder.LivePlan(Enumerable.Repeat(0.5f, 300).ToArray(), 300, 3, 0.7, usePressure: true);

        var piece = Assert.Single(plan);
        Assert.Equal(3f, piece.Width, 3);
    }

    [Fact]
    public void ARisingPressure_GivesGrowingPieces_ThatCoverTheWholeStroke_InAFewLines()
    {
        var plan = FreehandBuilder.LivePlan(Ramp(2000, 0.1f, 0.9f), 2000, 3, 0.7, usePressure: true);

        Assert.True(plan.Count is > 3 and < 60, $"{plan.Count} pieces");
        Assert.Equal(0, plan[0].From);
        Assert.Equal(1999, plan[^1].To);
        for (var i = 1; i < plan.Count; i++)
        {
            Assert.Equal(plan[i - 1].To, plan[i].From); // the next piece starts where the last ended
            Assert.True(plan[i].Width >= plan[i - 1].Width - 0.01f, "the width grows with the pressure");
        }

        Assert.True(plan[^1].Width > plan[0].Width * 2);
    }

    [Fact]
    public void ANoisyPressure_DoesNotMakeThousandsOfPieces()
    {
        var random = new Random(3);
        var pressure = Enumerable.Range(0, 3000).Select(i => 0.5f + (float)(random.NextDouble() - 0.5) * 0.05f).ToArray();

        var plan = FreehandBuilder.LivePlan(pressure, 3000, 3, 0.7, usePressure: true);

        Assert.True(plan.Count < 15, $"{plan.Count} pieces");
    }

    [Fact]
    public void NoPoints_NoPieces_AndAFewPointsAreOnePiece()
    {
        Assert.Empty(FreehandBuilder.LivePlan([], 0, 3, 0.7, usePressure: true));
        Assert.Single(FreehandBuilder.LivePlan([0.2f, 0.9f], 2, 3, 0.7, usePressure: true));
    }

    [Fact]
    public void AMissingPressure_FallsBackToThePenWidth()
    {
        // fewer pressures than points (the first reading of a pen may be missing): never a crash, the pen width
        var plan = FreehandBuilder.LivePlan([0.2f], 100, 4, 0.7, usePressure: true);

        Assert.Equal(4f, Assert.Single(plan).Width);
    }
}
