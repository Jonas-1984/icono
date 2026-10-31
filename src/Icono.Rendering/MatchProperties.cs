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
    public static bool Copy(Shape source, Shape target, MatchAspects requested, int? sourceSegment = null, int? targetSegment = null)
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
            target.TextHAlign = source.TextHAlign;
            target.TextVAlign = source.TextVAlign;
            target.TextPlacement = source.TextPlacement;
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
            CopyDimensionLook(source, target, sourceSegment, targetSegment);
        }

        if (aspects.HasFlag(MatchAspects.Size))
        {
            target.Width = source.Width;
            target.Height = source.Height;
        }

        return !ShapeSnapshot.Capture(target).Equals(before);
    }

    /// <summary>Distance of the dimension line from its object, where the number sits on the line and how far it is off the line - for a wall the
    /// values of the dimension of <paramref name="segment"/> (what was edited for that one dimension), else of the wall as a whole. Without a
    /// segment the only segment of the wall that was edited is taken.</summary>
    public static (double Offset, double? TextPosition, double TextShift) EffectiveDimensionPlacement(Shape source, int? segment)
    {
        if (source.Kind != ShapeKind.Wall)
        {
            return (source.DimOffset, source.DimTextPosition, source.DimTextShift);
        }

        var edited = source.WallDimensionOverrides?.Where(o => o.Offset is not null || o.TextPosition is not null || o.TextShift is not null).ToList() ?? [];
        var chosen = segment is { } index
            ? WallRenderer.FindOverride(source, index)
            : edited.Count == 1 ? edited[0] : null;
        return (chosen?.Offset ?? source.DimOffset, chosen?.TextPosition ?? source.DimTextPosition, chosen?.TextShift ?? source.DimTextShift);
    }

    private static void CopyDimensionLook(Shape source, Shape target, int? sourceSegment, int? targetSegment)
    {
        var (offset, textPosition, textShift) = EffectiveDimensionPlacement(source, sourceSegment);
        var onlyThatDimension = target.Kind == ShapeKind.Wall && targetSegment is not null;
        // a free-standing dimension keeps its own place (its offset is where it sits, not a style); walls, doors and windows take the distance
        if (!onlyThatDimension && target.Kind != ShapeKind.Dimension)
        {
            target.DimOffset = offset;
        }

        target.DimFontSize = source.DimFontSize;
        target.DimLineWidth = source.DimLineWidth;
        target.DimArrowSize = source.DimArrowSize;
        target.DimExtGap = source.DimExtGap;
        target.DimExtOvershoot = source.DimExtOvershoot;
        target.DimArrowStyle = source.DimArrowStyle;
        if (onlyThatDimension)
        {
            // one dimension of a wall was clicked: only that one gets the distance and number position (as its own settings)
            var list = (target.WallDimensionOverrides ?? []).ToList();
            var index = list.FindIndex(o => o.Segment == targetSegment);
            var current = index >= 0 ? list[index] : new WallDimensionOverride(targetSegment!.Value);
            var updated = current with { Offset = offset, TextPosition = textPosition, TextShift = textShift };
            if (index >= 0)
            {
                list[index] = updated;
            }
            else
            {
                list.Add(updated);
            }

            target.WallDimensionOverrides = list.OrderBy(o => o.Segment).ToList();
        }
        else
        {
            target.DimTextPosition = textPosition;
            target.DimTextShift = textShift;
        }

        // a wall whose single dimensions were edited takes the new distance and number position for all of them
        if (!onlyThatDimension && target.Kind == ShapeKind.Wall && target.WallDimensionOverrides is { Count: > 0 } overrides)
        {
            var kept = overrides.Select(o => o with { Offset = null, TextPosition = null, TextShift = null }).Where(o => !o.IsDefault).ToList();
            target.WallDimensionOverrides = kept.Count == 0 ? null : kept;
        }

        if (target.Kind == ShapeKind.Dimension || ArchitecturalSymbols.IsSymbol(target.Kind))
        {
            // unit and decimals are what a dimension (or the dimensions of a door or window) IS written in; a wall keeps its own unit switch (see the Wall aspect)
            target.DimUnit = source.Kind == ShapeKind.Wall ? source.WallDimensionUnit : source.DimUnit;
            target.DimPrecision = source.DimPrecision;
        }
        else if (target.Kind == ShapeKind.Wall)
        {
            target.WallDimensionUnit = source.Kind == ShapeKind.Dimension || ArchitecturalSymbols.IsSymbol(source.Kind) ? source.DimUnit : source.WallDimensionUnit;
        }
    }

    /// <summary>True for the shapes that carry a dimension style: dimensions, walls and the doors, windows and openings with their own dimensions.</summary>
    public static bool HasDimensionStyle(Shape shape) => Kinds(shape).HasFlag(MatchAspects.Dimension);

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
            case ShapeKind.DoorSingle:
            case ShapeKind.DoorDouble:
            case ShapeKind.WindowSingle:
            case ShapeKind.WindowDouble:
            case ShapeKind.WallOpening:
                return MatchAspects.Fill | MatchAspects.Border | MatchAspects.Effects | MatchAspects.Dimension;
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
