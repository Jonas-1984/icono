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
    ArgbColor BorderColor,
    double BorderThickness,
    BorderStyleKind BorderStyleKind,
    ArgbColor BorderGradientColor,
    double BorderBlurRadius,
    double CornerRadius,
    double Opacity,
    double BlurRadius,
    double FadeAmount,
    bool ShadowEnabled,
    double ShadowDistance,
    double ShadowStrength,
    double ShadowAngle,
    ArgbColor ShadowColor,
    double Rotation,
    bool FlipHorizontal,
    bool FlipVertical,
    IReadOnlyList<CorePoint>? Points,
    bool Dashed,
    PathRoutingStyle PathRoutingStyle,
    double Depth3D,
    string? CustomPathData,
    bool Locked,
    Guid? GroupId,
    string? TextContent,
    double TextFontSize,
    bool TextBold,
    bool TextItalic)
{
    public static ShapeSnapshot Capture(Shape shape) => new(
        shape.X,
        shape.Y,
        shape.Width,
        shape.Height,
        shape.FillColor,
        shape.BorderColor,
        shape.BorderThickness,
        shape.BorderStyleKind,
        shape.BorderGradientColor,
        shape.BorderBlurRadius,
        shape.CornerRadius,
        shape.Opacity,
        shape.BlurRadius,
        shape.FadeAmount,
        shape.ShadowEnabled,
        shape.ShadowDistance,
        shape.ShadowStrength,
        shape.ShadowAngle,
        shape.ShadowColor,
        shape.Rotation,
        shape.FlipHorizontal,
        shape.FlipVertical,
        shape.Points,
        shape.Dashed,
        shape.PathRoutingStyle,
        shape.Depth3D,
        shape.CustomPathData,
        shape.Locked,
        shape.GroupId,
        shape.TextContent,
        shape.TextFontSize,
        shape.TextBold,
        shape.TextItalic);

    public void ApplyTo(Shape shape)
    {
        shape.X = X;
        shape.Y = Y;
        shape.Width = Width;
        shape.Height = Height;
        shape.FillColor = FillColor;
        shape.BorderColor = BorderColor;
        shape.BorderThickness = BorderThickness;
        shape.BorderStyleKind = BorderStyleKind;
        shape.BorderGradientColor = BorderGradientColor;
        shape.BorderBlurRadius = BorderBlurRadius;
        shape.CornerRadius = CornerRadius;
        shape.Opacity = Opacity;
        shape.BlurRadius = BlurRadius;
        shape.FadeAmount = FadeAmount;
        shape.ShadowEnabled = ShadowEnabled;
        shape.ShadowDistance = ShadowDistance;
        shape.ShadowStrength = ShadowStrength;
        shape.ShadowAngle = ShadowAngle;
        shape.ShadowColor = ShadowColor;
        shape.Rotation = Rotation;
        shape.FlipHorizontal = FlipHorizontal;
        shape.FlipVertical = FlipVertical;
        shape.Points = Points;
        shape.Dashed = Dashed;
        shape.PathRoutingStyle = PathRoutingStyle;
        shape.Depth3D = Depth3D;
        shape.CustomPathData = CustomPathData;
        shape.Locked = Locked;
        shape.GroupId = GroupId;
        shape.TextContent = TextContent;
        shape.TextFontSize = TextFontSize;
        shape.TextBold = TextBold;
        shape.TextItalic = TextItalic;
    }
}
