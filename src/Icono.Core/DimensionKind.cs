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
}
