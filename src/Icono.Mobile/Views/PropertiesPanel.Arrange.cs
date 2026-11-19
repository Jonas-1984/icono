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

    /// <summary>Shapes with straight sides and no points of their own: for angles and lengths they become shapes of points first (as when their
    /// corners are edited), so that every corner and every side can be measured.</summary>
    private static readonly HashSet<ShapeKind> StraightSided =
    [
        ShapeKind.Rectangle, ShapeKind.Triangle, ShapeKind.Diamond, ShapeKind.Pentagon, ShapeKind.Hexagon, ShapeKind.Octagon,
        ShapeKind.Parallelogram, ShapeKind.Trapezoid, ShapeKind.Star, ShapeKind.Cross,
    ];

    /// <summary>"Add dimensions" for the selection; a box shape is made a shape of points for angles and lengths, a round measure on something that
    /// is no circle says so.</summary>
    private static void AddDimensions(EditorContext context, DimensionSet set)
    {
        if (set is DimensionSet.Corners or DimensionSet.Segments)
        {
            foreach (var shape in context.SelectedShapes.Where(s => s.Points is null && StraightSided.Contains(s.Kind)).ToList())
            {
                context.ConvertToEditablePoints(shape);
            }
        }
        else if (set == DimensionSet.Round && !context.SelectedShapes.Any(s => s.Kind == ShapeKind.Ellipse))
        {
            context.Notify("Round measures (diameter, radius) are for circles and ellipses.");
            return;
        }

        context.AddDimensionsToSelection(set);
    }

    /// <summary>Union: selected walls are joined into one wall (the outline of a wall as a plain shape would be no wall any more), anything else is united into one shape.</summary>
    private static void UnionOrJoin(EditorContext context)
    {
        if (context.SelectedShapes.Count >= 2 && context.SelectedShapes.All(s => s.Kind == ShapeKind.Wall))
        {
            context.JoinSelectedWalls();
        }
        else
        {
            context.UnionSelection();
        }
    }

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
        if (shapes.Any(PolylineClosing.CanClose))
        {
            // an open polyline: its two ends are joined and it becomes a closed shape that takes a fill
            ButtonRow(("Close shape", () => PolylineClosing.CloseSelected(context)));
        }

        Heading("COMBINE");
        ButtonRow(
            ("Union", () => UnionOrJoin(context)),
            ("Merge", context.MergeSelection),
            ("Trim", context.TrimSelection),
            ("Explode", context.ExplodeSelection));
        Note("Union: the outline of all of them as one shape (walls that are selected are joined into one wall instead). Merge: one shape from all of them. Trim: the second cuts the first. " +
             "Explode: a combined object (a sink, a cabinet) falls apart into its pieces.");

        Heading("MOVE");
        NumberRow("Right", _moveX, UnitName, 5 * UnitFactor, -100000, 100000, "0.##", v => _moveX = v);
        NumberRow("Down", _moveY, UnitName, 5 * UnitFactor, -100000, 100000, "0.##", v => _moveY = v);
        ButtonRow(("Move", () => context.NudgeSelection(FromReal(_moveX), FromReal(_moveY))));

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
            ButtonRow(("Union (join walls)", context.JoinSelectedWalls), ("Close room", context.CloseSelectedWalls));
            ButtonRow(("Merge straight", context.MergeStraightWallParts), ("Split at corners", context.SplitSelectedWallsAtAllPoints));
            if (shapes.Count(s => s.Kind == ShapeKind.Wall) >= 2)
            {
                Note("Union: two or more walls that follow each other end to end become ONE wall (a gap between them is bridged). Walls that meet in a T cannot be one wall; they are drawn joined anyway.");
            }
        }

        Heading("ADD DIMENSIONS");
        ButtonRow(
            ("Automatic", () => context.AddDimensionsToSelection(DimensionSet.Auto)),
            ("Width, height", () => context.AddDimensionsToSelection(DimensionSet.Size)),
            ("Round", () => AddDimensions(context, DimensionSet.Round)));
        ButtonRow(
            ("Angles", () => AddDimensions(context, DimensionSet.Corners)),
            ("Lengths", () => AddDimensions(context, DimensionSet.Segments)));
        Note("Round: for circles and ellipses. Angles and Lengths: for shapes with straight sides (a rectangle or triangle becomes editable points first) and for lines with several points.");

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
