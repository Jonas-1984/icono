namespace Icono.Core;

public sealed class Shape : ObservableObject
{
    private double _x;
    private double _y;
    private double _width = 80;
    private double _height = 80;
    private ArgbColor _fillColor = ArgbColor.FromRgb(0x9E, 0xD9, 0xD1);
    private ArgbColor _borderColor = ArgbColor.FromRgb(0x4A, 0x8F, 0x9E);
    private double _borderThickness = 2;
    private BorderStyleKind _borderStyleKind = BorderStyleKind.Solid;
    private ArgbColor _borderGradientColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
    private double _borderBlurRadius;
    private double _cornerRadius;
    private double _opacity = 100;
    private double _blurRadius;
    private double _fadeAmount;
    private bool _shadowEnabled;
    private double _shadowDistance = 4;
    private double _shadowStrength = 40;
    private double _shadowAngle = 45;
    private ArgbColor _shadowColor = ArgbColor.FromRgb(0, 0, 0);
    private double _rotation;
    private bool _flipHorizontal;
    private bool _flipVertical;
    private IReadOnlyList<CorePoint>? _points;
    private bool _dashed;
    private PathRoutingStyle _pathRoutingStyle = PathRoutingStyle.Straight;
    private double _depth3D = 12;
    private string? _customPathData;
    private bool _locked;
    private Guid? _groupId;
    private string? _textContent;
    private double _textFontSize = 24;
    private bool _textBold;
    private bool _textItalic;

    public required ShapeKind Kind { get; init; }

    public double X { get => _x; set => SetField(ref _x, value); }
    public double Y { get => _y; set => SetField(ref _y, value); }
    public double Width { get => _width; set => SetField(ref _width, value); }
    public double Height { get => _height; set => SetField(ref _height, value); }

    public ArgbColor FillColor { get => _fillColor; set => SetField(ref _fillColor, value); }
    public ArgbColor BorderColor { get => _borderColor; set => SetField(ref _borderColor, value); }
    public double BorderThickness { get => _borderThickness; set => SetField(ref _borderThickness, value); }

    /// <summary>Solid/Gradient/Fade — mirrors the Fade-vs-Opacity distinction the fill already has.</summary>
    public BorderStyleKind BorderStyleKind { get => _borderStyleKind; set => SetField(ref _borderStyleKind, value); }

    /// <summary>Second gradient stop, only used when <see cref="BorderStyleKind"/> is <see cref="Icono.Core.BorderStyleKind.Gradient"/>.</summary>
    public ArgbColor BorderGradientColor { get => _borderGradientColor; set => SetField(ref _borderGradientColor, value); }

    /// <summary>Blur applied to the stroke only, independent from <see cref="BlurRadius"/> so the
    /// border can be frosted while the fill stays crisp (or vice versa).</summary>
    public double BorderBlurRadius { get => _borderBlurRadius; set => SetField(ref _borderBlurRadius, value); }

    /// <summary>Corner rounding radius, applied to every polygon-based <see cref="ShapeKind"/> (0 = sharp corners).</summary>
    public double CornerRadius { get => _cornerRadius; set => SetField(ref _cornerRadius, value); }

    /// <summary>0-100. Uniform alpha applied to the whole shape.</summary>
    public double Opacity { get => _opacity; set => SetField(ref _opacity, value); }

    /// <summary>Gaussian blur radius in px, 0 = none.</summary>
    public double BlurRadius { get => _blurRadius; set => SetField(ref _blurRadius, value); }

    /// <summary>
    /// 0-100. Unlike <see cref="Opacity"/> (uniform dimming), Fade is a one-directional alpha
    /// gradient: the shape stays solid at its top edge and progressively becomes transparent
    /// toward the bottom edge. The value controls how much of the shape's height that
    /// fade-out covers (0 = no fade, 100 = fades from solid top to fully invisible bottom).
    /// </summary>
    public double FadeAmount { get => _fadeAmount; set => SetField(ref _fadeAmount, value); }

    public bool ShadowEnabled { get => _shadowEnabled; set => SetField(ref _shadowEnabled, value); }

    /// <summary>Down-right offset in px, applied equally to dx and dy.</summary>
    public double ShadowDistance { get => _shadowDistance; set => SetField(ref _shadowDistance, value); }

    /// <summary>0-100. Drives both the shadow's blur amount and its opacity.</summary>
    public double ShadowStrength { get => _shadowStrength; set => SetField(ref _shadowStrength, value); }

    /// <summary>Degrees, clockwise from due right (0°), the direction the shadow is cast in. Wraps into [0, 360).</summary>
    public double ShadowAngle { get => _shadowAngle; set => SetField(ref _shadowAngle, ((value % 360) + 360) % 360); }

    public ArgbColor ShadowColor { get => _shadowColor; set => SetField(ref _shadowColor, value); }

    /// <summary>Degrees, clockwise, around the shape's own center. Wraps into [0, 360).</summary>
    public double Rotation { get => _rotation; set => SetField(ref _rotation, ((value % 360) + 360) % 360); }

    public bool FlipHorizontal { get => _flipHorizontal; set => SetField(ref _flipHorizontal, value); }

    public bool FlipVertical { get => _flipVertical; set => SetField(ref _flipVertical, value); }

    /// <summary>Null for every fixed-geometry <see cref="ShapeKind"/> (rectangles, polygons, etc.),
    /// which stay driven purely by X/Y/Width/Height as before. Non-null for the line-family kinds
    /// (Line, Arrow, DoubleArrow, Polyline, Curve) — an ordered list of anchor points, stored
    /// shape-local (point (0,0) = this shape's X,Y), kept in sync with X/Y/Width/Height by
    /// <c>Icono.Rendering.PathBoundsCalculator.Recompute</c> after every point add/move/delete.</summary>
    public IReadOnlyList<CorePoint>? Points { get => _points; set => SetField(ref _points, value); }

    /// <summary>Dashed stroke, only meaningful for <see cref="Points"/>-based kinds.</summary>
    public bool Dashed { get => _dashed; set => SetField(ref _dashed, value); }

    /// <summary>How a <see cref="Points"/>-based shape's segments are routed — Straight (direct
    /// segments), Orthogonal (right-angle "elbow" bends), or Curved (smooth through every anchor).</summary>
    public PathRoutingStyle PathRoutingStyle { get => _pathRoutingStyle; set => SetField(ref _pathRoutingStyle, value); }

    /// <summary>3D extrusion depth in px, only meaningful for <see cref="ShapeKind.Box3D"/>.</summary>
    public double Depth3D { get => _depth3D; set => SetField(ref _depth3D, Math.Max(0, value)); }

    /// <summary>SVG path data for a <see cref="ShapeKind.CombinedPath"/> — the result of a
    /// Union/Trim boolean operation on two or more shapes, normalized into the same local 24x24
    /// space every fixed-geometry kind uses, so it flows through the same scale/offset/rotate
    /// pipeline in <c>Icono.Rendering.ShapeGeometryFactory.Build</c>.</summary>
    public string? CustomPathData { get => _customPathData; set => SetField(ref _customPathData, value); }

    /// <summary>When true, this shape can't be moved/resized/rotated/deleted (still selectable
    /// and visible, so the user can unlock it again).</summary>
    public bool Locked { get => _locked; set => SetField(ref _locked, value); }

    /// <summary>Shapes sharing the same non-null GroupId act as one unit: clicking any one of
    /// them selects the whole group. Null = not grouped.</summary>
    public Guid? GroupId { get => _groupId; set => SetField(ref _groupId, value); }

    /// <summary>The actual typed text for a <see cref="ShapeKind.Text"/> shape. Null/empty means
    /// "not yet typed" — such a shape renders as the plain placeholder glyph instead (see
    /// <c>Icono.Rendering.ShapeRenderer.Draw</c>).</summary>
    public string? TextContent { get => _textContent; set => SetField(ref _textContent, value); }

    /// <summary>Font size in px, only meaningful once <see cref="TextContent"/> is set.</summary>
    public double TextFontSize { get => _textFontSize; set => SetField(ref _textFontSize, value); }

    public bool TextBold { get => _textBold; set => SetField(ref _textBold, value); }

    public bool TextItalic { get => _textItalic; set => SetField(ref _textItalic, value); }
}
