namespace Icono.Core;

/// <summary>What a <see cref="ShapeKind.Dimension"/> measures (the AutoCAD dimension types).</summary>
public enum DimensionKind
{
    /// <summary>Distance along the x axis between two points.</summary>
    Horizontal,

    /// <summary>Distance along the y axis between two points.</summary>
    Vertical,

    /// <summary>Straight distance between two points, along the line joining them.</summary>
    Aligned,

    /// <summary>Angle between two lines meeting at a vertex.</summary>
    Angular,

    /// <summary>Radius of a circle or arc.</summary>
    Radius,

    /// <summary>Diameter of a circle.</summary>
    Diameter,

    /// <summary>Length of a curve or a whole outline, written next to it on a leader.</summary>
    Length,
}

/// <summary>Which part of its target shape an automatically created dimension follows.</summary>
public enum DimensionRole
{
    /// <summary>A free dimension: its points are its own.</summary>
    None,
    Width,
    Height,
    Depth,
    Diameter,
    Radius,
    Length,

    /// <summary>One segment of a polyline-like shape (see <see cref="Shape.DimIndex"/>).</summary>
    Segment,

    /// <summary>The angle at one corner of a polyline-like shape (see <see cref="Shape.DimIndex"/>).</summary>
    Corner,

    /// <summary>The shortest distance between two objects (<see cref="Shape.DimTarget"/> and <see cref="Shape.DimTarget2"/>).</summary>
    Gap,
}

/// <summary>The mark at the two ends of a dimension line.</summary>
public enum DimensionArrowStyle
{
    /// <summary>A filled arrowhead (the AutoCAD default).</summary>
    Arrow,

    /// <summary>An arrowhead drawn as two lines.</summary>
    OpenArrow,

    /// <summary>A slash "/" across the line (architectural tick).</summary>
    Slash,

    /// <summary>A backslash "\" across the line.</summary>
    Backslash,

    /// <summary>Two slashes "//" across the line.</summary>
    DoubleSlash,

    /// <summary>A filled dot.</summary>
    Dot,

    /// <summary>A small ring.</summary>
    OpenDot,

    /// <summary>A small open square.</summary>
    Box,

    /// <summary>No mark: the line just ends.</summary>
    None,
}
