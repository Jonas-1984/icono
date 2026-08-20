namespace Icono.Core;

/// <summary>How a Points-based shape's segments are routed between anchors.</summary>
public enum PathRoutingStyle
{
    Straight,
    Orthogonal,
    Curved,

    /// <summary>Same right-angle "elbow" routing as <see cref="Orthogonal"/>, but with each bend
    /// rounded off instead of a sharp corner.</summary>
    Rounded,
}
