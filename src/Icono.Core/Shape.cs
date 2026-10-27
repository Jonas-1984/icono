namespace Icono.Core;

public sealed class Shape : ObservableObject
{
    private double _x;
    private double _y;
    private double _width = 80;
    private double _height = 80;
    private ArgbColor _fillColor = ArgbColor.FromRgb(0x9E, 0xD9, 0xD1);
    private bool _fillEnabled = true;
    private DesignStyleKind _designStyle = DesignStyleKind.None;
    private GradientFill? _fillGradient;
    private double _gridCellSize = DesignGridMath.DefaultCellSizePx;
    private bool _gridShowLines = true;
    private IReadOnlyDictionary<int, ArgbColor>? _gridCells;
    private ArgbColor _borderColor = ArgbColor.FromRgb(0x4A, 0x8F, 0x9E);
    private double _borderThickness = 2;
    private BorderStyleKind _borderStyleKind = BorderStyleKind.Solid;
    private ArgbColor _borderGradientColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
    private double _borderBlurRadius;
    private double _cornerRadius;
    private double _opacity = 100;
    private double _blurRadius;
    private double _fadeAmount;
    private FadeDirection _fadeDirection = FadeDirection.TopToBottom;
    private bool _shadowEnabled;
    private double _shadowDistance = 4;
    private double _shadowStrength = 40;
    private double _shadowBlurRadius = 8;
    private double _shadowAngle = 45;
    private ArgbColor _shadowColor = ArgbColor.FromRgb(0, 0, 0);
    private double _rotation;
    private bool _flipHorizontal;
    private bool _flipVertical;
    private IReadOnlyList<CorePoint>? _points;
    private bool _dashed;
    private PathRoutingStyle _pathRoutingStyle = PathRoutingStyle.Straight;
    private ArrowHeadKind _lineStartHead = ArrowHeadKind.None;
    private ArrowHeadKind _lineEndHead = ArrowHeadKind.None;
    private double _depth3D = 12;
    private string? _customPathData;
    private bool _customPathStrokeOnly;
    private byte[]? _imageBytes;
    private bool _locked;
    private Guid? _groupId;
    private string? _name;
    private string? _textContent;
    private string? _textFontFamily;
    private double _textFontSize = 24;
    private bool _textBold;
    private bool _textItalic;
    private bool _textUnderline;
    private TextHorizontalAlign _textHAlign;
    private TextVerticalAlign _textVAlign;
    private ArgbColor? _textColor;
    private double _wallThickness = 16;
    private bool _wallClosed;
    private HatchKind _hatch = HatchKind.Diagonal;
    private double _hatchSpacing = 8;
    private ArgbColor _hatchColor = ArgbColor.FromRgb(0x5B, 0x63, 0x70);
    private bool _wallShowDimensions = true;
    private LengthUnit _wallDimensionUnit = LengthUnit.Centimeter;
    private DimensionKind _dimKind = DimensionKind.Aligned;
    private DimensionRole _dimRole;
    private Guid? _dimTarget;
    private Guid? _dimTarget2;
    private int _dimIndex;
    private double _dimOffset = 28;
    private double _dimAngle = -45;
    private LengthUnit _dimUnit = LengthUnit.Centimeter;
    private int _dimPrecision = 2;
    private string? _dimText;
    private double _dimValue;
    private IReadOnlyList<ShapeAnchor?>? _dimAnchors;
    private double? _dimFontSize;
    private double? _dimLineWidth;
    private double? _dimArrowSize;
    private double? _dimExtGap;
    private double? _dimExtOvershoot;
    private DimensionArrowStyle _dimArrowStyle;
    private double? _dimTextPosition;
    private double _dimTextShift;
    private IReadOnlyList<WallDimensionOverride>? _wallDimensionOverrides;
    private WallMeasure _wallDimensionMeasure = WallMeasure.Inside;
    private WallDimensionPlacement _wallDimensionPlacement;
    private double _doorOpenAngle = 90;
    private bool _symbolShowDimension = true;
    private bool _symbolShowGaps = true;
    private ShapeAnchor? _startAnchor;
    private ShapeAnchor? _endAnchor;

    public required ShapeKind Kind { get; init; }

    /// <summary>Stable identity, so a line can say which shape it is attached to (see <see cref="StartAnchor"/>). Not part of
    /// the undo snapshot: a clone is a new shape and gets its own id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public double X { get => _x; set => SetField(ref _x, value); }
    public double Y { get => _y; set => SetField(ref _y, value); }
    public double Width { get => _width; set => SetField(ref _width, value); }
    public double Height { get => _height; set => SetField(ref _height, value); }

    public ArgbColor FillColor
    {
        get => _fillColor;
        set
        {
            if (SetField(ref _fillColor, value) && _fillGradient is not null)
            {
                // A deliberately chosen flat colour replaces an imported gradient.
                FillGradient = null;
            }
        }
    }

    /// <summary>False = draw no fill at all, only the border (and shadow/blur, if any) — for a
    /// shape that should read as an outline only, without switching its <see cref="ShapeKind"/> to
    /// a stroke-only kind. No effect on kinds that never draw a fill anyway (Line, Arrow, ...).</summary>
    public bool FillEnabled { get => _fillEnabled; set => SetField(ref _fillEnabled, value); }

    /// <summary>Optional UI design language (Glassmorphism, Neumorphism, ...) the shape is rendered in. None = the
    /// normal fill/border look. Only affects closed, fillable shapes — see <c>DesignStyleRenderer.Supports</c>.</summary>
    public DesignStyleKind DesignStyle { get => _designStyle; set => SetField(ref _designStyle, value); }

    /// <summary>Optional gradient fill (e.g. from an imported SVG) drawn instead of the flat <see cref="FillColor"/>. Picking a
    /// new fill colour clears it.</summary>
    public GradientFill? FillGradient { get => _fillGradient; set => SetField(ref _fillGradient, value); }

    /// <summary>Edge length of one square of a <see cref="ShapeKind.DesignGrid"/>, in px (default 2 mm at 96 DPI).</summary>
    public double GridCellSize { get => _gridCellSize; set => SetField(ref _gridCellSize, Math.Max(0.5, value)); }

    /// <summary>Whether a <see cref="ShapeKind.DesignGrid"/> draws its grid lines. Switching this off leaves the coloured
    /// squares behind as a pure pixel picture.</summary>
    public bool GridShowLines { get => _gridShowLines; set => SetField(ref _gridShowLines, value); }

    /// <summary>Colours of the painted squares of a <see cref="ShapeKind.DesignGrid"/>, keyed by <see cref="DesignGridMath.Key"/>.
    /// Treated as IMMUTABLE: every edit assigns a fresh dictionary, which is what lets undo snapshots just keep the reference.</summary>
    public IReadOnlyDictionary<int, ArgbColor>? GridCells { get => _gridCells; set => SetField(ref _gridCells, value); }
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

    /// <summary>Which way the fade runs — see <see cref="Icono.Core.FadeDirection"/>. Defaults to
    /// TopToBottom, matching the fill fade's original (and only, before this was added) behavior.</summary>
    public FadeDirection FadeDirection { get => _fadeDirection; set => SetField(ref _fadeDirection, value); }

    public bool ShadowEnabled { get => _shadowEnabled; set => SetField(ref _shadowEnabled, value); }

    /// <summary>Down-right offset in px, applied equally to dx and dy.</summary>
    public double ShadowDistance { get => _shadowDistance; set => SetField(ref _shadowDistance, value); }

    /// <summary>0-100. The shadow's opacity — 100 is fully solid ShadowColor, independent of
    /// <see cref="ShadowBlurRadius"/> so a shadow can be crisp AND solid black at the same time.</summary>
    public double ShadowStrength { get => _shadowStrength; set => SetField(ref _shadowStrength, value); }

    /// <summary>Gaussian blur radius in px, 0 = a perfectly sharp-edged shadow. Separate from
    /// <see cref="ShadowStrength"/> so sharpness and opacity/darkness can be tuned independently.</summary>
    public double ShadowBlurRadius { get => _shadowBlurRadius; set => SetField(ref _shadowBlurRadius, Math.Max(0, value)); }

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

    /// <summary>Marker at the FIRST anchor point, only meaningful for a <see cref="Points"/>-based
    /// shape (Line, Arrow, DoubleArrow, Polyline, Curve). None = a plain line end.</summary>
    public ArrowHeadKind LineStartHead { get => _lineStartHead; set => SetField(ref _lineStartHead, value); }

    /// <summary>Marker at the LAST anchor point — see <see cref="LineStartHead"/>.</summary>
    public ArrowHeadKind LineEndHead { get => _lineEndHead; set => SetField(ref _lineEndHead, value); }

    /// <summary>Connection point of another shape the line's FIRST point is glued to (null = free end). The line follows the
    /// shape when that moves; see <c>Icono.Rendering.ConnectionRouter</c>.</summary>
    public ShapeAnchor? StartAnchor { get => _startAnchor; set => SetField(ref _startAnchor, value); }

    /// <summary>Same as <see cref="StartAnchor"/>, for the LAST point.</summary>
    public ShapeAnchor? EndAnchor { get => _endAnchor; set => SetField(ref _endAnchor, value); }

    /// <summary>3D extrusion depth in px, only meaningful for <see cref="ShapeKind.Box3D"/>.</summary>
    public double Depth3D { get => _depth3D; set => SetField(ref _depth3D, Math.Max(0, value)); }

    /// <summary>SVG path data for a <see cref="ShapeKind.CombinedPath"/> — the result of a
    /// Union/Trim boolean operation on two or more shapes, normalized into the same local 24x24
    /// space every fixed-geometry kind uses, so it flows through the same scale/offset/rotate
    /// pipeline in <c>Icono.Rendering.ShapeGeometryFactory.Build</c>.</summary>
    public string? CustomPathData { get => _customPathData; set => SetField(ref _customPathData, value); }

    /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="ShapeKind.CombinedPath"/>.
    /// True for a path imported from a stroke-style SVG (fill="none", the Lucide/Feather/Tabler
    /// convention — see <c>Icono.Rendering.SvgImporter.IsStrokeStyleSvg</c>), so it renders as an
    /// outline like the fixed-geometry icon glyph kinds instead of a solid fill. False (the default)
    /// for a <c>Icono.Rendering.ShapeCombiner</c> Union/Trim result, which should stay filled.</summary>
    public bool CustomPathStrokeOnly { get => _customPathStrokeOnly; set => SetField(ref _customPathStrokeOnly, value); }

    /// <summary>PNG-encoded pixel data for a <see cref="ShapeKind.RasterImage"/> — set once at
    /// import time (see IconLibraryView's "Import Image" flow, which converts a source .ico's
    /// largest frame to PNG so this is always PNG regardless of the original file type) and drawn
    /// stretched to the shape's X/Y/Width/Height by <c>Icono.Rendering.ShapeRenderer.Draw</c>.</summary>
    public byte[]? ImageBytes { get => _imageBytes; set => SetField(ref _imageBytes, value); }

    /// <summary>When true, this shape can't be moved/resized/rotated/deleted (still selectable
    /// and visible, so the user can unlock it again).</summary>
    public bool Locked { get => _locked; set => SetField(ref _locked, value); }

    /// <summary>Shapes sharing the same non-null GroupId act as one unit: clicking any one of
    /// them selects the whole group. Null = not grouped.</summary>
    public Guid? GroupId { get => _groupId; set => SetField(ref _groupId, value); }

    /// <summary>User-given name shown in the Layers panel's shape list. Null/empty means "not
    /// renamed yet" — the panel falls back to an auto-generated "{Kind} {index}" label instead
    /// (see <c>Icono.App.Views.ShapeListItem</c>).</summary>
    public string? Name { get => _name; set => SetField(ref _name, value); }

    /// <summary>The actual typed text for a <see cref="ShapeKind.Text"/> shape. Null/empty means
    /// "not yet typed" — such a shape renders as the plain placeholder glyph instead (see
    /// <c>Icono.Rendering.ShapeRenderer.Draw</c>).</summary>
    public string? TextContent { get => _textContent; set => SetField(ref _textContent, value); }

    /// <summary>Windows font family name (e.g. "Segoe UI", "Arial"). Null/empty means "not chosen
    /// yet" — falls back to "Segoe UI" (see <c>Icono.Rendering.ShapeRenderer.DrawTextShape</c>).</summary>
    public string? TextFontFamily { get => _textFontFamily; set => SetField(ref _textFontFamily, value); }

    /// <summary>Font size in px, only meaningful once <see cref="TextContent"/> is set.</summary>
    public double TextFontSize { get => _textFontSize; set => SetField(ref _textFontSize, value); }

    public bool TextBold { get => _textBold; set => SetField(ref _textBold, value); }

    public bool TextItalic { get => _textItalic; set => SetField(ref _textItalic, value); }

    public bool TextUnderline { get => _textUnderline; set => SetField(ref _textUnderline, value); }

    /// <summary>How the lines of the text are aligned in the shape (Center = the classic look).</summary>
    public TextHorizontalAlign TextHAlign { get => _textHAlign; set => SetField(ref _textHAlign, value); }

    /// <summary>Where the text block sits in the shape: top, middle or bottom.</summary>
    public TextVerticalAlign TextVAlign { get => _textVAlign; set => SetField(ref _textVAlign, value); }

    /// <summary>Colour of the text typed INTO a shape (double-click). Null = automatic: dark or white, whichever reads
    /// better on the shape's fill. A <see cref="ShapeKind.Text"/> shape is coloured by its fill instead.</summary>
    public ArgbColor? TextColor { get => _textColor; set => SetField(ref _textColor, value); }

    /// <summary>Thickness of a <see cref="ShapeKind.Wall"/> in px (the band is centred on its points).</summary>
    public double WallThickness { get => _wallThickness; set => SetField(ref _wallThickness, Math.Max(0.5, value)); }

    /// <summary>True when the wall runs back to its first point and forms a closed ring.</summary>
    public bool WallClosed { get => _wallClosed; set => SetField(ref _wallClosed, value); }

    public HatchKind Hatch { get => _hatch; set => SetField(ref _hatch, value); }

    /// <summary>Distance between hatch lines (or dots/bricks) in px.</summary>
    public double HatchSpacing { get => _hatchSpacing; set => SetField(ref _hatchSpacing, Math.Max(2, value)); }

    public ArgbColor HatchColor { get => _hatchColor; set => SetField(ref _hatchColor, value); }

    /// <summary>Whether the length of every wall segment is written next to it.</summary>
    public bool WallShowDimensions { get => _wallShowDimensions; set => SetField(ref _wallShowDimensions, value); }

    public LengthUnit WallDimensionUnit { get => _wallDimensionUnit; set => SetField(ref _wallDimensionUnit, value); }

    /// <summary>What a <see cref="ShapeKind.Dimension"/> measures.</summary>
    public DimensionKind DimKind { get => _dimKind; set => SetField(ref _dimKind, value); }

    /// <summary>The part of <see cref="DimTarget"/> an automatic dimension follows (None for a free dimension).</summary>
    public DimensionRole DimRole { get => _dimRole; set => SetField(ref _dimRole, value); }

    /// <summary>The shape this dimension is attached to; it then moves and changes with it. Null = free.</summary>
    public Guid? DimTarget { get => _dimTarget; set => SetField(ref _dimTarget, value); }

    /// <summary>The second object of a <see cref="DimensionRole.Gap"/> dimension.</summary>
    public Guid? DimTarget2 { get => _dimTarget2; set => SetField(ref _dimTarget2, value); }

    /// <summary>Segment or corner number for <see cref="DimensionRole.Segment"/> / <see cref="DimensionRole.Corner"/>.</summary>
    public int DimIndex { get => _dimIndex; set => SetField(ref _dimIndex, value); }

    /// <summary>How far (px) the dimension line sits from what it measures, for an attached dimension.</summary>
    public double DimOffset { get => _dimOffset; set => SetField(ref _dimOffset, value); }

    /// <summary>Direction (degrees) of the leader of an attached radius/diameter dimension.</summary>
    public double DimAngle { get => _dimAngle; set => SetField(ref _dimAngle, value); }

    public LengthUnit DimUnit { get => _dimUnit; set => SetField(ref _dimUnit, value); }

    /// <summary>Decimal places of the written value.</summary>
    public int DimPrecision { get => _dimPrecision; set => SetField(ref _dimPrecision, Math.Clamp(value, 0, 6)); }

    /// <summary>Replacement text; "&lt;&gt;" inside it stands for the measured value (like AutoCAD). Null/empty = measured value only.</summary>
    public string? DimText { get => _dimText; set => SetField(ref _dimText, value); }

    /// <summary>Measured value (px) for kinds that cannot be derived from the dimension's own points (curve length, 3D depth).</summary>
    public double DimValue { get => _dimValue; set => SetField(ref _dimValue, value); }

    /// <summary>For a dimension placed by hand: which connection point, corner or centre of another shape each of its points sits on
    /// (parallel to <see cref="Shape.Points"/>, null = free point). Attached points follow their shape; see <c>DimensionRouter</c>.</summary>
    public IReadOnlyList<ShapeAnchor?>? DimAnchors { get => _dimAnchors; set => SetField(ref _dimAnchors, value); }

    // Look of the dimension lines of a <see cref="ShapeKind.Dimension"/> and of the segment dimensions of a <see cref="ShapeKind.Wall"/>.
    // Null = automatic.

    /// <summary>Size of the written value in px.</summary>
    public double? DimFontSize { get => _dimFontSize; set => SetField(ref _dimFontSize, value); }

    /// <summary>Width of the dimension, extension and leader lines in px.</summary>
    public double? DimLineWidth { get => _dimLineWidth; set => SetField(ref _dimLineWidth, value); }

    /// <summary>Length of the arrowheads in px.</summary>
    public double? DimArrowSize { get => _dimArrowSize; set => SetField(ref _dimArrowSize, value); }

    /// <summary>Gap in px between the measured point and the start of its extension line.</summary>
    public double? DimExtGap { get => _dimExtGap; set => SetField(ref _dimExtGap, value); }

    /// <summary>How far in px the extension lines run past the dimension line.</summary>
    public double? DimExtOvershoot { get => _dimExtOvershoot; set => SetField(ref _dimExtOvershoot, value); }

    /// <summary>The mark at both ends of the dimension line: arrowhead, slash, backslash, double slash, dot ... or nothing.</summary>
    public DimensionArrowStyle DimArrowStyle { get => _dimArrowStyle; set => SetField(ref _dimArrowStyle, value); }

    /// <summary>Where the number sits along the dimension line, 0 (start) to 1 (end); null = the middle. For a wall: the default of its dimensions.</summary>
    public double? DimTextPosition { get => _dimTextPosition; set => SetField(ref _dimTextPosition, value); }

    /// <summary>How far in px the number is moved off the dimension line (positive = to the upper side of the text).</summary>
    public double DimTextShift { get => _dimTextShift; set => SetField(ref _dimTextShift, value); }

    /// <summary>Settings of single dimensions of a <see cref="ShapeKind.Wall"/> (one per segment, only where they differ).</summary>
    public IReadOnlyList<WallDimensionOverride>? WallDimensionOverrides { get => _wallDimensionOverrides; set => SetField(ref _wallDimensionOverrides, value); }

    /// <summary>What the wall's dimensions measure: the inside (clear size of a room), the centre line or the outside.</summary>
    public WallMeasure WallDimensionMeasure { get => _wallDimensionMeasure; set => SetField(ref _wallDimensionMeasure, value); }

    /// <summary>Whether the dimension lines of the wall run inside the plan, outside it, or are placed automatically.</summary>
    public WallDimensionPlacement WallDimensionPlacement { get => _wallDimensionPlacement; set => SetField(ref _wallDimensionPlacement, value); }

    /// <summary>How far the leaf of a door symbol is open, in degrees (0 = closed, 90 = at a right angle to the wall).</summary>
    public double DoorOpenAngle { get => _doorOpenAngle; set => SetField(ref _doorOpenAngle, Math.Clamp(value, 0, 120)); }

    /// <summary>Whether a door, window or wall opening shows the width of its opening as a dimension next to it.</summary>
    public bool SymbolShowDimension { get => _symbolShowDimension; set => SetField(ref _symbolShowDimension, value); }

    /// <summary>Whether a door, window or wall opening in a wall shows the clear distances from its jambs to the corners of the wall.</summary>
    public bool SymbolShowGaps { get => _symbolShowGaps; set => SetField(ref _symbolShowGaps, value); }
}
