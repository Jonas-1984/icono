using Icono.Core;

namespace Icono.Rendering;

/// <summary>The groups of properties the Match tool can carry from one object to another.</summary>
[Flags]
public enum MatchAspects
{
    None = 0,

    /// <summary>Fill colour, gradient and whether the shape is filled at all.</summary>
    Fill = 1,

    /// <summary>Border / line colour, thickness, style, gradient and blur.</summary>
    Border = 2,

    /// <summary>Opacity, blur, fade and shadow.</summary>
    Effects = 4,

    /// <summary>Corner rounding, design style (glass, neumorphism ...) and 3D depth.</summary>
    Form = 8,

    /// <summary>Font, size, bold / italic / underline and colour of the text.</summary>
    Text = 16,

    /// <summary>Dashed stroke, routing and the markers at the ends of a line.</summary>
    Line = 32,

    /// <summary>Thickness, hatch and dimension switches of a wall.</summary>
    Wall = 64,

    /// <summary>Look of dimension lines: unit, precision, text size, line width, end marks and number position.</summary>
    Dimension = 128,

    /// <summary>Width and height (the object keeps its position and rotation).</summary>
    Size = 256,

    /// <summary>Everything that is about looks; the size is only matched when asked for.</summary>
    Look = Fill | Border | Effects | Form | Text | Line | Wall | Dimension,
}

/// <summary>Copies the look of one shape onto another (like AutoCAD's MATCHPROP or a format painter). Only what makes sense for the target
/// is copied: a line never gets a fill, a rectangle never gets arrowheads. Position, geometry, rotation, name, lock, group and
/// attachments stay as they are.</summary>
public static class MatchProperties
{
    /// <summary>Copies the requested aspects that both shapes understand. Returns false when nothing changed.</summary>
    public static bool Copy(Shape source, Shape target, MatchAspects requested)
    {
        if (ReferenceEquals(source, target))
        {
            return false;
        }

        var aspects = requested & Kinds(source) & Kinds(target);
        var before = ShapeSnapshot.Capture(target);

        if (aspects.HasFlag(MatchAspects.Fill))
        {
            target.FillColor = source.FillColor;
            target.FillEnabled = source.FillEnabled;
            target.FillGradient = source.FillGradient;
        }

        if (aspects.HasFlag(MatchAspects.Border))
        {
            target.BorderColor = source.BorderColor;
            target.BorderThickness = source.BorderThickness;
            target.BorderStyleKind = source.BorderStyleKind;
            target.BorderGradientColor = source.BorderGradientColor;
            target.BorderBlurRadius = source.BorderBlurRadius;
        }

        if (aspects.HasFlag(MatchAspects.Effects))
        {
            target.Opacity = source.Opacity;
            target.BlurRadius = source.BlurRadius;
            target.FadeAmount = source.FadeAmount;
            target.FadeDirection = source.FadeDirection;
            target.ShadowEnabled = source.ShadowEnabled;
            target.ShadowDistance = source.ShadowDistance;
            target.ShadowStrength = source.ShadowStrength;
            target.ShadowBlurRadius = source.ShadowBlurRadius;
            target.ShadowAngle = source.ShadowAngle;
            target.ShadowColor = source.ShadowColor;
        }

        if (aspects.HasFlag(MatchAspects.Form))
        {
            target.CornerRadius = source.CornerRadius;
            target.DesignStyle = source.DesignStyle;
            if (source.Kind == ShapeKind.Box3D && target.Kind == ShapeKind.Box3D)
            {
                target.Depth3D = source.Depth3D;
            }
        }

        if (aspects.HasFlag(MatchAspects.Text))
        {
            target.TextFontFamily = source.TextFontFamily;
            target.TextFontSize = source.TextFontSize;
            target.TextBold = source.TextBold;
            target.TextItalic = source.TextItalic;
            target.TextUnderline = source.TextUnderline;
            target.TextColor = source.TextColor;
        }

        if (aspects.HasFlag(MatchAspects.Line))
        {
            target.Dashed = source.Dashed;
            target.LineStartHead = source.LineStartHead;
            target.LineEndHead = source.LineEndHead;
        }

        if (aspects.HasFlag(MatchAspects.Wall))
        {
            target.WallThickness = source.WallThickness;
            target.Hatch = source.Hatch;
            target.HatchSpacing = source.HatchSpacing;
            target.HatchColor = source.HatchColor;
            target.WallShowDimensions = source.WallShowDimensions;
            target.WallDimensionMeasure = source.WallDimensionMeasure;
            target.WallDimensionPlacement = source.WallDimensionPlacement;
            target.WallDimensionUnit = source.WallDimensionUnit;
        }

        if (aspects.HasFlag(MatchAspects.Dimension))
        {
            CopyDimensionLook(source, target);
        }

        if (aspects.HasFlag(MatchAspects.Size))
        {
            target.Width = source.Width;
            target.Height = source.Height;
        }

        return !ShapeSnapshot.Capture(target).Equals(before);
    }

    private static void CopyDimensionLook(Shape source, Shape target)
    {
        target.DimFontSize = source.DimFontSize;
        target.DimLineWidth = source.DimLineWidth;
        target.DimArrowSize = source.DimArrowSize;
        target.DimExtGap = source.DimExtGap;
        target.DimExtOvershoot = source.DimExtOvershoot;
        target.DimArrowStyle = source.DimArrowStyle;
        target.DimTextPosition = source.DimTextPosition;
        target.DimTextShift = source.DimTextShift;

        if (target.Kind == ShapeKind.Dimension)
        {
            // unit and decimals are what a dimension IS written in; a wall keeps its own unit switch (see the Wall aspect)
            target.DimUnit = source.Kind == ShapeKind.Wall ? source.WallDimensionUnit : source.DimUnit;
            target.DimPrecision = source.DimPrecision;
        }
        else if (target.Kind == ShapeKind.Wall)
        {
            target.WallDimensionUnit = source.Kind == ShapeKind.Dimension ? source.DimUnit : source.WallDimensionUnit;
        }
    }

    /// <summary>The aspects that mean something for this kind of shape (as source or as target).</summary>
    private static MatchAspects Kinds(Shape shape)
    {
        switch (shape.Kind)
        {
            case ShapeKind.RasterImage:
            case ShapeKind.DesignGrid:
                return MatchAspects.Effects;
            case ShapeKind.Dimension:
                return MatchAspects.Border | MatchAspects.Effects | MatchAspects.Dimension;
            case ShapeKind.Wall:
                return MatchAspects.Fill | MatchAspects.Border | MatchAspects.Effects | MatchAspects.Wall | MatchAspects.Dimension;
            case ShapeKind.Text:
                return MatchAspects.Fill | MatchAspects.Border | MatchAspects.Effects | MatchAspects.Form | MatchAspects.Text;
        }

        if (ShapeGeometryFactory.IsLineFamily(shape.Kind))
        {
            return MatchAspects.Border | MatchAspects.Effects | MatchAspects.Line;
        }

        return MatchAspects.Fill | MatchAspects.Border | MatchAspects.Effects | MatchAspects.Form | MatchAspects.Text | MatchAspects.Size;
    }
}
