using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>What can be done with the selected objects, as in the menus of the Windows program (Arrange, Draw, Dimension): turn, flip, combine,
/// explode, move by a distance, align, the helpers for walls and the dimensions. All of it is the shared <see cref="EditorContext"/>.</summary>
public sealed partial class PropertiesPanel
{
    /// <summary>The distance of the "Move" card (real size, cm): kept while the panel is built again.</summary>
    private double _moveX;

    private double _moveY;

    private void BuildArrange(EditorContext context, IReadOnlyList<Shape> shapes)
    {
        Heading("ARRANGE");
        ButtonRow(
            ("Turn 90°", () => context.RotateSelection(90)),
            ("Turn -90°", () => context.RotateSelection(-90)),
            ("Turn 180°", () => context.RotateSelection(180)));
        ButtonRow(
            ("Flip ↔", () => context.FlipSelection(horizontally: true)),
            ("Flip ↕", () => context.FlipSelection(horizontally: false)),
            ("Reflect", context.ReflectSelection));
        ButtonRow(("Offset...", () => AskOffset(context)), ("Save as icon", () => SaveAsIcon(context)));

        Heading("COMBINE");
        ButtonRow(
            ("Union", context.UnionSelection),
            ("Merge", context.MergeSelection),
            ("Trim", context.TrimSelection),
            ("Explode", context.ExplodeSelection));
        Note("Union: the outline of all of them as one shape. Merge: one shape from all of them. Trim: the second cuts the first. " +
             "Explode: a combined object (a sink, a cabinet) falls apart into its pieces.");

        Heading("MOVE");
        NumberRow("Right", _moveX, "cm", 5, -100000, 100000, "0.#", cm => _moveX = cm);
        NumberRow("Down", _moveY, "cm", 5, -100000, 100000, "0.#", cm => _moveY = cm);
        ButtonRow(("Move", () => context.NudgeSelection(FromRealCm(_moveX), FromRealCm(_moveY))));

        // one object aligns to the page, several to each other; three or more can also be spread evenly
        Heading(shapes.Count == 1 ? "ALIGN TO PAGE" : "ALIGN");
        ButtonRow(
            ("Left", () => context.AlignSelection(AlignKind.Left)),
            ("Centre", () => context.AlignSelection(AlignKind.CenterHorizontally)),
            ("Right", () => context.AlignSelection(AlignKind.Right)));
        ButtonRow(
            ("Top", () => context.AlignSelection(AlignKind.Top)),
            ("Middle", () => context.AlignSelection(AlignKind.CenterVertically)),
            ("Bottom", () => context.AlignSelection(AlignKind.Bottom)));
        if (shapes.Count >= 3)
        {
            ButtonRow(
                ("Spread ↔", () => context.DistributeSelection(horizontally: true)),
                ("Spread ↕", () => context.DistributeSelection(horizontally: false)));
        }

        if (shapes.Any(s => s.Kind == ShapeKind.Wall))
        {
            Heading("WALLS");
            ButtonRow(
                ("Close room", context.CloseSelectedWalls),
                ("Join walls", context.JoinSelectedWalls),
                ("Merge straight", context.MergeStraightWallParts));
            ButtonRow(("Split at every corner", context.SplitSelectedWallsAtAllPoints));
        }

        Heading("ADD DIMENSIONS");
        ButtonRow(
            ("Automatic", () => context.AddDimensionsToSelection(DimensionSet.Auto)),
            ("Width, height", () => context.AddDimensionsToSelection(DimensionSet.Size)),
            ("Round", () => context.AddDimensionsToSelection(DimensionSet.Round)));
        ButtonRow(
            ("Angles", () => context.AddDimensionsToSelection(DimensionSet.Corners)),
            ("Lengths", () => context.AddDimensionsToSelection(DimensionSet.Segments)));

        Heading("DOOR & WINDOW MEASURES");
        ButtonRow(
            ("Show", () => context.SetSymbolDimensions(show: true, all: false)),
            ("Hide", () => context.SetSymbolDimensions(show: false, all: false)));
        ButtonRow(
            ("Show all", () => context.SetSymbolDimensions(show: true, all: true)),
            ("Hide all", () => context.SetSymbolDimensions(show: false, all: true)));

        ButtonRow(("Select all", context.SelectAll), ("Deselect", context.ClearSelection));
    }
}
