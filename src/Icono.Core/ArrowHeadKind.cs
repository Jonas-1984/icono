namespace Icono.Core;

/// <summary>Marker drawn at one end of a Points-based line/arrow shape (see
/// <see cref="Shape.LineStartHead"/>/<see cref="Shape.LineEndHead"/>). Independent per end, so a
/// single line can e.g. have a filled arrow at one end and a plain circle at the other.</summary>
public enum ArrowHeadKind
{
    None,
    Open,
    Filled,
    Thin,
    Half,
    Circle,
    Diamond,
    Square,
    Bar,
}
