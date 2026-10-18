namespace Icono.Core;

public enum GradientKind
{
    Linear,
    Radial,
}

/// <summary>One colour stop of a <see cref="GradientFill"/>; <see cref="Offset"/> runs 0..1.</summary>
public sealed record GradientStop(double Offset, ArgbColor Color);

/// <summary>
/// A gradient fill for a shape (imported from an SVG linearGradient / radialGradient). The gradient geometry
/// (<see cref="X1"/>.. for linear, <see cref="Cx"/>.. for radial) lives in "gradient space"; the SVG-style matrix
/// <see cref="A"/>..<see cref="F"/> (x' = A*x + C*y + E, y' = B*x + D*y + F) carries it into the shape's local 24 x 24
/// path space, so the gradient scales, flips and rotates together with the shape. Immutable.
/// </summary>
public sealed record GradientFill(
    GradientKind Kind,
    double X1, double Y1, double X2, double Y2,
    double Cx, double Cy, double R, double Fx, double Fy,
    double A, double B, double C, double D, double E, double F,
    IReadOnlyList<GradientStop> Stops);
