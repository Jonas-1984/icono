namespace Icono.Core;

/// <summary>Settings of ONE dimension of a wall (the length written next to one of its segments). Everything left at its default follows
/// the wall's own dimension settings, and nothing set here reaches any other dimension: the look of one dimension is carried to others only
/// with the Match tool. Segments are numbered along the wall from 0; a closed wall's last segment runs back to the start.</summary>
/// <param name="Segment">Which segment of the wall.</param>
/// <param name="Hidden">Do not show this dimension.</param>
/// <param name="Offset">Distance of the dimension line from the wall's outer face, in px (null = the wall's distance).</param>
/// <param name="Flip">Put the dimension on the other side of the wall.</param>
/// <param name="TextPosition">Where the number sits along the line, 0 to 1 (null = the wall's setting, normally the middle).</param>
/// <param name="TextShift">How far the number is moved off the line, in px (null = the wall's setting).</param>
/// <param name="Text">Your own text; "&lt;&gt;" stands for the measured value (null = the measured value).</param>
/// <param name="Unit">The unit this dimension is written in (null = the wall's unit).</param>
/// <param name="Precision">Decimal places of this dimension (null = the wall's setting).</param>
/// <param name="ArrowStyle">The marks at the ends of this dimension line (null = the wall's setting).</param>
/// <param name="FontSize">Size of the number in px (null = the wall's setting).</param>
/// <param name="LineWidth">Width of the dimension and extension lines in px (null = the wall's setting).</param>
/// <param name="ArrowSize">Length of the arrowheads in px (null = the wall's setting).</param>
/// <param name="ExtGap">Space between the measured point and the extension line, in px (null = the wall's setting).</param>
/// <param name="ExtOvershoot">How far the extension lines run past the dimension line, in px (null = the wall's setting).</param>
public sealed record WallDimensionOverride(
    int Segment, bool Hidden = false, double? Offset = null, bool Flip = false,
    double? TextPosition = null, double? TextShift = null, string? Text = null,
    LengthUnit? Unit = null, int? Precision = null, DimensionArrowStyle? ArrowStyle = null,
    double? FontSize = null, double? LineWidth = null, double? ArrowSize = null, double? ExtGap = null, double? ExtOvershoot = null)
{
    /// <summary>True when this dimension has its own look (unit, decimals, marks, sizes), as opposed to its place.</summary>
    public bool HasOwnStyle =>
        Unit is not null || Precision is not null || ArrowStyle is not null || FontSize is not null || LineWidth is not null ||
        ArrowSize is not null || ExtGap is not null || ExtOvershoot is not null;

    /// <summary>The same dimension with no look of its own (its place and text stay).</summary>
    public WallDimensionOverride WithoutStyle() =>
        this with { Unit = null, Precision = null, ArrowStyle = null, FontSize = null, LineWidth = null, ArrowSize = null, ExtGap = null, ExtOvershoot = null };

    /// <summary>True when nothing differs from the wall's settings (such an entry can be dropped).</summary>
    public bool IsDefault => !Hidden && Offset is null && !Flip && TextPosition is null && TextShift is null && string.IsNullOrEmpty(Text) && !HasOwnStyle;
}
