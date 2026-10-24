namespace Icono.Core;

/// <summary>Where one end of a line/arrow is attached: a connection point (see
/// <c>Icono.Rendering.ConnectionPoints</c>) of another shape, found by that shape's <see cref="Shape.Id"/>.</summary>
public readonly record struct ShapeAnchor(Guid ShapeId, int Index);
