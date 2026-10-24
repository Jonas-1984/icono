namespace Icono.Core;

/// <summary>Settings of ONE dimension of a wall (the length written next to one of its segments). Everything left at its default follows
/// the wall's own dimension settings. Segments are numbered along the wall from 0; a closed wall's last segment runs back to the start.</summary>
/// <param name="Segment">Which segment of the wall.</param>
/// <param name="Hidden">Do not show this dimension.</param>
/// <param name="Offset">Distance of the dimension line from the wall's outer face, in px (null = the wall's distance).</param>
/// <param name="Flip">Put the dimension on the other side of the wall.</param>
/// <param name="TextPosition">Where the number sits along the line, 0 to 1 (null = the wall's setting, normally the middle).</param>
/// <param name="TextShift">How far the number is moved off the line, in px (null = the wall's setting).</param>
/// <param name="Text">Your own text; "&lt;&gt;" stands for the measured value (null = the measured value).</param>
public sealed record WallDimensionOverride(
    int Segment, bool Hidden = false, double? Offset = null, bool Flip = false,
    double? TextPosition = null, double? TextShift = null, string? Text = null)
{
    /// <summary>True when nothing differs from the wall's settings (such an entry can be dropped).</summary>
    public bool IsDefault => !Hidden && Offset is null && !Flip && TextPosition is null && TextShift is null && string.IsNullOrEmpty(Text);
}
