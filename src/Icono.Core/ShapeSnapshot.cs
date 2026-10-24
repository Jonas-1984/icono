namespace Icono.Core;

/// <summary>
/// A full value-copy of a <see cref="Shape"/>'s mutable state. Cheap to take since every
/// mutable Shape property is itself a value type — this is what makes snapshot-based undo/redo
/// affordable without a Command-per-mutation-site rewrite.
/// </summary>
public readonly record struct ShapeSnapshot(
    double X,
    double Y,
    double Width,
    double Height,
    ArgbColor FillColor,
    bool FillEnabled,
    DesignStyleKind DesignStyle,
    GradientFill? FillGradient,
    double GridCellSize,
    bool GridShowLines,
    IReadOnlyDictionary<int, ArgbColor>? GridCells,
    ArgbColor BorderColor,
    double BorderThickness,
    BorderStyleKind BorderStyleKind,
    ArgbColor BorderGradientColor,
    double BorderBlurRadius,
    double CornerRadius,
    double Opacity,
    double BlurRadius,
    double FadeAmount,
    FadeDirection FadeDirection,
    bool ShadowEnabled,
    double ShadowDistance,
    double ShadowStrength,
    double ShadowBlurRadius,
    double ShadowAngle,
    ArgbColor ShadowColor,
    double Rotation,
    bool FlipHorizontal,
    bool FlipVertical,
    IReadOnlyList<CorePoint>? Points,
    bool Dashed,
    PathRoutingStyle PathRoutingStyle,
    ArrowHeadKind LineStartHead,
    ArrowHeadKind LineEndHead,
    double Depth3D,
    string? CustomPathData,
    bool Locked,
    Guid? GroupId,
    string? TextContent,
    string? TextFontFamily,
    double TextFontSize,
    bool TextBold,
    bool TextItalic,
    bool TextUnderline,
    string? Name,
    ShapeAnchor? StartAnchor = null,
    ShapeAnchor? EndAnchor = null,
    ArgbColor? TextColor = null,
    double WallThickness = 16,
    bool WallClosed = false,
    HatchKind Hatch = HatchKind.Diagonal,
    double HatchSpacing = 8,
    ArgbColor HatchColor = default,
    bool WallShowDimensions = true,
    LengthUnit WallDimensionUnit = LengthUnit.Centimeter,
    DimensionKind DimKind = DimensionKind.Aligned,
    DimensionRole DimRole = DimensionRole.None,
    Guid? DimTarget = null,
    int DimIndex = 0,
    double DimOffset = 28,
    double DimAngle = -45,
    LengthUnit DimUnit = LengthUnit.Centimeter,
    int DimPrecision = 2,
    string? DimText = null,
    double DimValue = 0,
    IReadOnlyList<ShapeAnchor?>? DimAnchors = null,
    double? DimFontSize = null,
    double? DimLineWidth = null,
    double? DimArrowSize = null,
    double? DimExtGap = null,
    double? DimExtOvershoot = null,
    double? DimTextPosition = null,
    double DimTextShift = 0,
    IReadOnlyList<WallDimensionOverride>? WallDimensionOverrides = null,
    Guid? DimTarget2 = null,
    DimensionArrowStyle DimArrowStyle = DimensionArrowStyle.Arrow)
{
    public static ShapeSnapshot Capture(Shape shape) => new(
        shape.X,
        shape.Y,
        shape.Width,
        shape.Height,
        shape.FillColor,
        shape.FillEnabled,
        shape.DesignStyle,
        shape.FillGradient,
        shape.GridCellSize,
        shape.GridShowLines,
        shape.GridCells,
        shape.BorderColor,
        shape.BorderThickness,
        shape.BorderStyleKind,
        shape.BorderGradientColor,
        shape.BorderBlurRadius,
        shape.CornerRadius,
        shape.Opacity,
        shape.BlurRadius,
        shape.FadeAmount,
        shape.FadeDirection,
        shape.ShadowEnabled,
        shape.ShadowDistance,
        shape.ShadowStrength,
        shape.ShadowBlurRadius,
        shape.ShadowAngle,
        shape.ShadowColor,
        shape.Rotation,
        shape.FlipHorizontal,
        shape.FlipVertical,
        shape.Points,
        shape.Dashed,
        shape.PathRoutingStyle,
        shape.LineStartHead,
        shape.LineEndHead,
        shape.Depth3D,
        shape.CustomPathData,
        shape.Locked,
        shape.GroupId,
        shape.TextContent,
        shape.TextFontFamily,
        shape.TextFontSize,
        shape.TextBold,
        shape.TextItalic,
        shape.TextUnderline,
        shape.Name,
        shape.StartAnchor,
        shape.EndAnchor,
        shape.TextColor,
        shape.WallThickness,
        shape.WallClosed,
        shape.Hatch,
        shape.HatchSpacing,
        shape.HatchColor,
        shape.WallShowDimensions,
        shape.WallDimensionUnit,
        shape.DimKind,
        shape.DimRole,
        shape.DimTarget,
        shape.DimIndex,
        shape.DimOffset,
        shape.DimAngle,
        shape.DimUnit,
        shape.DimPrecision,
        shape.DimText,
        shape.DimValue,
        shape.DimAnchors,
        shape.DimFontSize,
        shape.DimLineWidth,
        shape.DimArrowSize,
        shape.DimExtGap,
        shape.DimExtOvershoot,
        shape.DimTextPosition,
        shape.DimTextShift,
        shape.WallDimensionOverrides,
        shape.DimTarget2,
        shape.DimArrowStyle);

    public void ApplyTo(Shape shape)
    {
        shape.X = X;
        shape.Y = Y;
        shape.Width = Width;
        shape.Height = Height;
        shape.FillColor = FillColor;
        shape.FillEnabled = FillEnabled;
        shape.DesignStyle = DesignStyle;
        shape.FillGradient = FillGradient;
        shape.GridCellSize = GridCellSize;
        shape.GridShowLines = GridShowLines;
        shape.GridCells = GridCells;
        shape.BorderColor = BorderColor;
        shape.BorderThickness = BorderThickness;
        shape.BorderStyleKind = BorderStyleKind;
        shape.BorderGradientColor = BorderGradientColor;
        shape.BorderBlurRadius = BorderBlurRadius;
        shape.CornerRadius = CornerRadius;
        shape.Opacity = Opacity;
        shape.BlurRadius = BlurRadius;
        shape.FadeAmount = FadeAmount;
        shape.FadeDirection = FadeDirection;
        shape.ShadowEnabled = ShadowEnabled;
        shape.ShadowDistance = ShadowDistance;
        shape.ShadowStrength = ShadowStrength;
        shape.ShadowBlurRadius = ShadowBlurRadius;
        shape.ShadowAngle = ShadowAngle;
        shape.ShadowColor = ShadowColor;
        shape.Rotation = Rotation;
        shape.FlipHorizontal = FlipHorizontal;
        shape.FlipVertical = FlipVertical;
        shape.Points = Points;
        shape.Dashed = Dashed;
        shape.PathRoutingStyle = PathRoutingStyle;
        shape.LineStartHead = LineStartHead;
        shape.LineEndHead = LineEndHead;
        shape.Depth3D = Depth3D;
        shape.CustomPathData = CustomPathData;
        shape.Locked = Locked;
        shape.GroupId = GroupId;
        shape.TextContent = TextContent;
        shape.TextFontFamily = TextFontFamily;
        shape.TextFontSize = TextFontSize;
        shape.TextBold = TextBold;
        shape.TextItalic = TextItalic;
        shape.TextUnderline = TextUnderline;
        shape.Name = Name;
        shape.StartAnchor = StartAnchor;
        shape.EndAnchor = EndAnchor;
        shape.TextColor = TextColor;
        shape.WallThickness = WallThickness;
        shape.WallClosed = WallClosed;
        shape.Hatch = Hatch;
        shape.HatchSpacing = HatchSpacing;
        shape.HatchColor = HatchColor;
        shape.WallShowDimensions = WallShowDimensions;
        shape.WallDimensionUnit = WallDimensionUnit;
        shape.DimKind = DimKind;
        shape.DimRole = DimRole;
        shape.DimTarget = DimTarget;
        shape.DimIndex = DimIndex;
        shape.DimOffset = DimOffset;
        shape.DimAngle = DimAngle;
        shape.DimUnit = DimUnit;
        shape.DimPrecision = DimPrecision;
        shape.DimText = DimText;
        shape.DimValue = DimValue;
        shape.DimAnchors = DimAnchors;
        shape.DimFontSize = DimFontSize;
        shape.DimLineWidth = DimLineWidth;
        shape.DimArrowSize = DimArrowSize;
        shape.DimExtGap = DimExtGap;
        shape.DimExtOvershoot = DimExtOvershoot;
        shape.DimTextPosition = DimTextPosition;
        shape.DimTextShift = DimTextShift;
        shape.WallDimensionOverrides = WallDimensionOverrides;
        shape.DimTarget2 = DimTarget2;
        shape.DimArrowStyle = DimArrowStyle;
    }
}
