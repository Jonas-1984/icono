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
