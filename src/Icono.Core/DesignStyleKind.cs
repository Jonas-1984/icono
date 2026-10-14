namespace Icono.Core;

/// <summary>
/// A whole-look "UI design language" a closed shape can be rendered in. The shape keeps its own
/// <see cref="Shape.FillColor"/> as the base colour; the style decides how that colour is lit,
/// shadowed, bordered and glowed (see <c>Icono.Rendering.DesignStyleRenderer</c>).
/// </summary>
public enum DesignStyleKind
{
    /// <summary>Plain fill + border, the app's normal rendering.</summary>
    None,
    Glassmorphism,
    Neumorphism,
    FluentDesign,
    AuroraGradients,
    BentoUI,
    Cyberpunk,
    Claymorphism3D,
    DarkNeon,
}
