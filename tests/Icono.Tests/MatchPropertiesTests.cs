using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class MatchPropertiesTests
{
    private static Shape Source() => new()
    {
        Kind = ShapeKind.Rectangle, X = 10, Y = 20, Width = 100, Height = 50,
        FillColor = ArgbColor.FromRgb(200, 30, 30), BorderColor = ArgbColor.FromRgb(10, 10, 10), BorderThickness = 5,
        CornerRadius = 12, Opacity = 60, ShadowEnabled = true, ShadowDistance = 9, TextFontSize = 30, TextBold = true,
    };

    [Fact]
    public void Look_IsCopied_ButPositionSizeRotationAndNameAreNot()
    {
        var source = Source();
        var target = new Shape { Kind = ShapeKind.Ellipse, X = 300, Y = 400, Width = 40, Height = 40, Rotation = 45, Name = "mine" };

        Assert.True(MatchProperties.Copy(source, target, MatchAspects.Look));

        Assert.Equal(source.FillColor, target.FillColor);
        Assert.Equal(5, target.BorderThickness);
        Assert.Equal(12, target.CornerRadius);
        Assert.Equal(60, target.Opacity);
        Assert.True(target.ShadowEnabled);
        Assert.Equal(30, target.TextFontSize);
        Assert.True(target.TextBold);
        Assert.Equal((300, 400, 40, 40), (target.X, target.Y, target.Width, target.Height));
        Assert.Equal(45, target.Rotation);
        Assert.Equal("mine", target.Name);
    }

    [Fact]
    public void OnlyTheRequestedAspectsAreCopied()
    {
        var source = Source();
        var target = new Shape { Kind = ShapeKind.Rectangle };
        var originalBorder = target.BorderColor;

        MatchProperties.Copy(source, target, MatchAspects.Fill);

        Assert.Equal(source.FillColor, target.FillColor);
        Assert.Equal(originalBorder, target.BorderColor);
        Assert.False(target.ShadowEnabled);
    }

    [Fact]
    public void Size_IsOnlyCopiedWhenAskedFor()
    {
        var source = Source();
        var target = new Shape { Kind = ShapeKind.Rectangle, X = 5, Y = 6, Width = 20, Height = 20 };

        MatchProperties.Copy(source, target, MatchAspects.Look);
        Assert.Equal((20, 20), (target.Width, target.Height));

        MatchProperties.Copy(source, target, MatchAspects.Size);
        Assert.Equal((100, 50), (target.Width, target.Height));
        Assert.Equal((5, 6), (target.X, target.Y));
    }

    [Fact]
    public void ALine_NeverGivesItsFillToAShape_AndAShapeNeverGivesCornersToALine()
    {
        var line = new Shape
        {
            Kind = ShapeKind.Line, Points = [new CorePoint(0, 0), new CorePoint(50, 0)], BorderColor = ArgbColor.FromRgb(0, 0, 255),
            BorderThickness = 4, Dashed = true, LineEndHead = ArrowHeadKind.Filled, FillColor = ArgbColor.FromRgb(1, 2, 3),
        };
        var rectangle = new Shape { Kind = ShapeKind.Rectangle, FillColor = ArgbColor.FromRgb(9, 9, 9), CornerRadius = 8 };

        MatchProperties.Copy(line, rectangle, MatchAspects.Look);

        Assert.Equal(ArgbColor.FromRgb(9, 9, 9), rectangle.FillColor);
        Assert.Equal(8, rectangle.CornerRadius);
        Assert.Equal(ArgbColor.FromRgb(0, 0, 255), rectangle.BorderColor);
        Assert.False(rectangle.Dashed);

        var otherLine = new Shape { Kind = ShapeKind.Arrow, Points = [new CorePoint(0, 0), new CorePoint(0, 30)], CornerRadius = 3 };
        MatchProperties.Copy(Source(), otherLine, MatchAspects.Look);

        Assert.Equal(3, otherLine.CornerRadius);
        Assert.Equal(5, otherLine.BorderThickness);
    }

    [Fact]
    public void LineStyle_GoesFromLineToLine()
    {
        var source = new Shape
        {
            Kind = ShapeKind.Arrow, Points = [new CorePoint(0, 0), new CorePoint(50, 0)], Dashed = true,
            LineStartHead = ArrowHeadKind.Circle, LineEndHead = ArrowHeadKind.Filled,
        };
        var target = new Shape { Kind = ShapeKind.Line, Points = [new CorePoint(0, 0), new CorePoint(0, 40)] };

        Assert.True(MatchProperties.Copy(source, target, MatchAspects.Look));

        Assert.True(target.Dashed);
        Assert.Equal(ArrowHeadKind.Circle, target.LineStartHead);
        Assert.Equal(ArrowHeadKind.Filled, target.LineEndHead);
        Assert.Equal(2, target.Points!.Count);
    }

    [Fact]
    public void WallToWall_CarriesThicknessHatchAndDimensionStyle()
    {
        var source = new Shape
        {
            Kind = ShapeKind.Wall, Points = [new CorePoint(0, 0), new CorePoint(100, 0)], WallThickness = 30, Hatch = HatchKind.CrossHatch,
            HatchSpacing = 14, WallShowDimensions = false, DimFontSize = 18, DimArrowStyle = DimensionArrowStyle.Slash,
        };
        var target = new Shape { Kind = ShapeKind.Wall, Points = [new CorePoint(0, 0), new CorePoint(0, 80)] };

        MatchProperties.Copy(source, target, MatchAspects.Look);

        Assert.Equal(30, target.WallThickness);
        Assert.Equal(HatchKind.CrossHatch, target.Hatch);
        Assert.Equal(14, target.HatchSpacing);
        Assert.False(target.WallShowDimensions);
        Assert.Equal(18, target.DimFontSize);
        Assert.Equal(DimensionArrowStyle.Slash, target.DimArrowStyle);
        Assert.Equal(new CorePoint(0, 80), target.Points![1]);
    }

    [Fact]
    public void DimensionToDimension_CarriesUnitPrecisionAndEndMarks_NotWhatItMeasures()
    {
        var source = new Shape
        {
            Kind = ShapeKind.Dimension, DimUnit = LengthUnit.Millimeter, DimPrecision = 0, DimArrowStyle = DimensionArrowStyle.Slash,
            DimTextPosition = 0.25, DimOffset = 99, DimText = "<> mm",
        };
        var targetId = Guid.NewGuid();
        var target = new Shape { Kind = ShapeKind.Dimension, DimTarget = targetId, DimOffset = 28 };

        MatchProperties.Copy(source, target, MatchAspects.Look);

        Assert.Equal(LengthUnit.Millimeter, target.DimUnit);
        Assert.Equal(0, target.DimPrecision);
        Assert.Equal(DimensionArrowStyle.Slash, target.DimArrowStyle);
        Assert.Equal(0.25, target.DimTextPosition);
        Assert.Equal(targetId, target.DimTarget);
        Assert.Equal(28, target.DimOffset);
        Assert.Null(target.DimText);
    }

    [Fact]
    public void ASolidColour_ReplacesATargetsGradient()
    {
        var source = Source();
        var target = new Shape { Kind = ShapeKind.Rectangle };
        target.FillGradient = new GradientFill(GradientKind.Linear, 0, 0, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0,
            [new GradientStop(0, ArgbColor.FromRgb(0, 0, 0)), new GradientStop(1, ArgbColor.FromRgb(255, 255, 255))]);

        MatchProperties.Copy(source, target, MatchAspects.Fill);

        Assert.Null(target.FillGradient);
        Assert.Equal(source.FillColor, target.FillColor);
    }

    [Fact]
    public void NothingChanges_WhenTheLookIsAlreadyTheSame_OrTheShapeIsItself()
    {
        var source = Source();
        var target = new Shape { Kind = ShapeKind.Rectangle };
        MatchProperties.Copy(source, target, MatchAspects.Look);

        Assert.False(MatchProperties.Copy(source, target, MatchAspects.Look));
        Assert.False(MatchProperties.Copy(source, source, MatchAspects.Look));
        Assert.False(MatchProperties.Copy(source, new Shape { Kind = ShapeKind.Rectangle }, MatchAspects.None));
    }
}
