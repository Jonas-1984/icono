namespace Icono.Core;

/// <summary>Which way <see cref="Shape.FadeAmount"/>'s alpha gradient runs across the fill. The
/// two "BothSides" modes stay solid in the middle and fade out toward both opposite edges —
/// splitting <see cref="Shape.FadeAmount"/> evenly between the two fade zones.</summary>
public enum FadeDirection
{
    TopToBottom,
    BottomToTop,
    LeftToRight,
    RightToLeft,
    VerticalBothSides,
    HorizontalBothSides,
}
