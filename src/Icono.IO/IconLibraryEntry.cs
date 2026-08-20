using Icono.Core;

namespace Icono.IO;

/// <summary>One saved library entry — a named group of shapes the user can drop back onto the
/// canvas later. Deliberately minimal: this is the seed of a growable icon library, not a full
/// document save/export format.</summary>
public sealed class IconLibraryEntry
{
    public required string Name { get; set; }

    public required IReadOnlyList<Shape> Shapes { get; set; }
}
