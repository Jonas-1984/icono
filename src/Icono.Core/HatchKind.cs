namespace Icono.Core;

/// <summary>Surface pattern ("Hatch") drawn inside a <see cref="ShapeKind.Wall"/>, the way floor plans mark materials.</summary>
public enum HatchKind
{
    /// <summary>Plain fill, no pattern.</summary>
    Solid,
    Diagonal,
    ReverseDiagonal,
    CrossHatch,
    Horizontal,
    Vertical,
    Grid,
    Dots,
    Brick,
    Zigzag,
}

/// <summary>Unit the dimensions of a wall are written in.</summary>
public enum LengthUnit
{
    Millimeter,
    Centimeter,
    Meter,
    Inch,
}

/// <summary>How the lines of a text lie between the left and right edge of its shape (or text frame).</summary>
public enum TextHorizontalAlign
{
    Center,
    Left,
    Right,

    /// <summary>Both edges flush: the gaps between the words are widened (the last line of a paragraph stays left).</summary>
    Justify,
}

/// <summary>Where the text block sits between the top and the bottom edge of its shape (or text frame).</summary>
public enum TextVerticalAlign
{
    Middle,
    Top,
    Bottom,
}
