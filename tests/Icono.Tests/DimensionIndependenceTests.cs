using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

/// <summary>The custom settings of one dimension stay with that dimension: nothing set for one wall dimension reaches the others, and a look
/// is carried from one dimension to another only by the Match tool.</summary>
public class DimensionIndependenceTests
{
    private static Shape Wall()
    {
        DrawingScale.Denominator = 1;
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16,
            Points = [new CorePoint(0, 0), new CorePoint(300, 0), new CorePoint(300, 200)],
        };
        PathBoundsCalculator.Recompute(wall);
        return wall;
    }

    private static DimensionLayout Layout(Shape wall, int segment) => WallRenderer.LayoutSegmentDimension(wall, segment)!;

    [Fact]
    public void ANumberSizeSetForOneDimension_IsNotUsedByTheOtherDimensionsOfTheWall()
    {
        var wall = Wall();
        var before = Layout(wall, 1).FontSize;

        wall.WallDimensionOverrides = [new WallDimensionOverride(0, FontSize: 22)];

        Assert.Equal(22, Layout(wall, 0).FontSize);
        Assert.Equal(before, Layout(wall, 1).FontSize);
    }

    [Fact]
    public void TheUnitAndDecimalPlaces_AreThoseOfEachDimension()
    {
        var wall = Wall();
        wall.WallDimensionOverrides = [new WallDimensionOverride(0, Unit: LengthUnit.Millimeter, Precision: 3)];

        var first = Layout(wall, 0).Text;
        var second = Layout(wall, 1).Text;

        Assert.EndsWith("mm", first);
        Assert.EndsWith("cm", second);
        Assert.Equal(LengthUnit.Millimeter, WallRenderer.SegmentUnit(wall, 0));
        Assert.Equal(LengthUnit.Centimeter, WallRenderer.SegmentUnit(wall, 1));
        Assert.Equal(3, WallRenderer.SegmentPrecision(wall, 0));
        Assert.Equal(wall.DimPrecision, WallRenderer.SegmentPrecision(wall, 1));
    }

    [Fact]
    public void TheEndMarksAndLineWidth_AreSetPerDimension()
    {
        var wall = Wall();
        wall.WallDimensionOverrides = [new WallDimensionOverride(1, ArrowStyle: DimensionArrowStyle.Slash, LineWidth: 3)];

        Assert.Equal(DimensionArrowStyle.Arrow, Layout(wall, 0).Style.ArrowStyle);
        Assert.Equal(DimensionArrowStyle.Slash, Layout(wall, 1).Style.ArrowStyle);
        Assert.Equal(3, Layout(wall, 1).Style.LineWidth);
        Assert.NotEqual(3, Layout(wall, 0).Style.LineWidth);
    }

    [Fact]
    public void AnArrowThatWasNeverSized_FollowsTheNewNumberSizeOfThatDimensionOnly()
    {
        var wall = Wall();
        wall.WallDimensionOverrides = [new WallDimensionOverride(0, FontSize: 40)];

        Assert.True(Layout(wall, 0).Style.ArrowSize > Layout(wall, 1).Style.ArrowSize);
    }

    [Fact]
    public void ADimensionWithOnlyALookOfItsOwn_IsNotDroppedAsADefault()
    {
        var own = new WallDimensionOverride(2, FontSize: 14);

        Assert.False(own.IsDefault);
        Assert.True(own.WithoutStyle().IsDefault);
        Assert.True(new WallDimensionOverride(2).IsDefault);
    }

    [Fact]
    public void Match_FromOneWallDimensionToOneOther_ChangesOnlyThatOne()
    {
        var source = Wall();
        source.WallDimensionOverrides = [new WallDimensionOverride(0, Unit: LengthUnit.Millimeter, FontSize: 18, ArrowStyle: DimensionArrowStyle.Dot)];
        var target = Wall();
        target.WallDimensionOverrides = [new WallDimensionOverride(0, FontSize: 30)];

        Assert.True(MatchProperties.Copy(source, target, MatchAspects.Dimension, 0, 1));

        var second = WallRenderer.FindOverride(target, 1)!;
        Assert.Equal(18, second.FontSize);
        Assert.Equal(LengthUnit.Millimeter, second.Unit);
        Assert.Equal(DimensionArrowStyle.Dot, second.ArrowStyle);
        Assert.Equal(30, WallRenderer.FindOverride(target, 0)!.FontSize); // the other dimension of the target keeps its own
        Assert.Null(target.DimFontSize);                                    // and the wall's own settings were not touched
        Assert.Equal(LengthUnit.Centimeter, target.WallDimensionUnit);
    }

    [Fact]
    public void Match_ToAWholeWall_GivesAllItsDimensionsTheLook_AndDropsTheirOwnSettings()
    {
        var source = Wall();
        source.WallDimensionOverrides = [new WallDimensionOverride(1, FontSize: 18, LineWidth: 2)];
        var target = Wall();
        target.WallDimensionOverrides = [new WallDimensionOverride(0, FontSize: 30, Text: "kitchen")];

        MatchProperties.Copy(source, target, MatchAspects.Dimension, 1, null);

        Assert.Equal(18, target.DimFontSize);
        Assert.Equal(2, target.DimLineWidth);
        Assert.Equal(18, Layout(target, 0).FontSize);
        Assert.Equal("kitchen", WallRenderer.FindOverride(target, 0)!.Text); // its own text is not a look
        Assert.Null(WallRenderer.FindOverride(target, 0)!.FontSize);
    }

    [Fact]
    public void Match_FromAWallDimensionToAFreeDimension_CarriesTheLookOfThatDimension()
    {
        var source = Wall();
        source.WallDimensionOverrides = [new WallDimensionOverride(1, Unit: LengthUnit.Inch, Precision: 2, FontSize: 16)];
        var target = new Shape { Kind = ShapeKind.Dimension, DimKind = DimensionKind.Aligned };

        MatchProperties.Copy(source, target, MatchAspects.Dimension, 1, null);

        Assert.Equal(LengthUnit.Inch, target.DimUnit);
        Assert.Equal(2, target.DimPrecision);
        Assert.Equal(16, target.DimFontSize);
    }
}

public class MatchWithinOneWallTests
{
    [Fact]
    public void Match_FromOneDimensionOfAWallToAnotherDimensionOfTheSameWall_Works()
    {
        DrawingScale.Denominator = 1;
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16, WallClosed = true,
            Points = [new CorePoint(0, 0), new CorePoint(400, 0), new CorePoint(400, 300), new CorePoint(0, 300)],
        };
        PathBoundsCalculator.Recompute(wall);
        wall.WallDimensionOverrides = [new WallDimensionOverride(0, FontSize: 20)];

        Assert.True(MatchProperties.Copy(wall, wall, MatchAspects.Dimension, 0, 2));

        Assert.Equal(20, WallRenderer.FindOverride(wall, 2)!.FontSize);
        Assert.Equal(20, WallRenderer.FindOverride(wall, 0)!.FontSize);
        Assert.Null(WallRenderer.FindOverride(wall, 1));
        Assert.Equal(20, WallRenderer.LayoutSegmentDimension(wall, 2)!.FontSize);
    }

    [Fact]
    public void TheNumberOfEveryDimensionOfARoom_IsFoundByAClickOnIt()
    {
        DrawingScale.Denominator = 1;
        var wall = new Shape
        {
            Kind = ShapeKind.Wall, X = 100, Y = 100, WallThickness = 16, WallClosed = true,
            Points = [new CorePoint(0, 0), new CorePoint(400, 0), new CorePoint(400, 300), new CorePoint(0, 300)],
        };
        PathBoundsCalculator.Recompute(wall);

        for (var segment = 0; segment < 4; segment++)
        {
            var at = WallRenderer.LayoutSegmentDimension(wall, segment)!.TextPosition;
            Assert.Equal(segment, WallRenderer.HitTestDimensionText(wall, at.X, at.Y, 3));
        }
    }
}
