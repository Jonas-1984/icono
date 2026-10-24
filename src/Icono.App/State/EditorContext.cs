using System.Collections.ObjectModel;
using Icono.App.State.Undo;
using Icono.Core;
using Icono.Rendering;

namespace Icono.App.State;

public enum ActiveTool
{
    Select,
    PanZoom,
    Shape,
    PathDraw,
    AddPoint,
    DeletePoint,
    RegionColor,
    Eyedropper,
    WallCut,
}

/// <summary>
/// Shared editor state — one instance created by MainWindow and handed to the Tools panel,
/// canvas, and Design panel. Plain object, no DI/MVVM framework: matches this project's
/// existing non-MVVM code-behind style.
/// </summary>
public sealed class EditorContext : ObservableObject
{
    private ActiveTool _activeTool = ActiveTool.Select;
    private ShapeKind _selectedShapeKind = ShapeKind.Rectangle;
    private DimensionKind _currentDimensionKind = DimensionKind.Aligned;
    private Layer? _selectedLayer;
    private ArgbColor _currentFillColor = ArgbColor.FromRgb(0x9E, 0xD9, 0xD1);
    private bool _showGrid;
    private bool _snapToGrid;
    private double _gridSize = 8;

    public Document Document { get; } = new();

    public UndoManager UndoManager { get; } = new();

    public ActiveTool ActiveTool { get => _activeTool; set => SetField(ref _activeTool, value); }

    public ShapeKind SelectedShapeKind { get => _selectedShapeKind; set => SetField(ref _selectedShapeKind, value); }

    /// <summary>Source of truth for selection. Index order has no meaning; multi-select is a set.</summary>
    public ObservableCollection<Shape> SelectedShapes { get; } = new();

    /// <summary>The last-touched selected shape, or null if nothing is selected. Setting it
    /// replaces the whole selection with just this one shape (or clears it, if null).</summary>
    public Shape? SelectedShape
    {
        get => SelectedShapes.Count > 0 ? SelectedShapes[^1] : null;
        set
        {
            SelectedShapes.Clear();
            if (value is not null)
            {
                SelectedShapes.Add(value);
            }
        }
    }

    public Layer? SelectedLayer { get => _selectedLayer; set => SetField(ref _selectedLayer, value); }

    public ArgbColor CurrentFillColor { get => _currentFillColor; set => SetField(ref _currentFillColor, value); }

    /// <summary>Holds clones of the last Cut/Copy selection, ready for Paste. Plain in-memory
    /// list (no OS clipboard integration) — scoped to this document/session only.</summary>
    public List<Shape> Clipboard { get; } = new();

    public bool ShowGrid { get => _showGrid; set => SetField(ref _showGrid, value); }

    public bool SnapToGrid { get => _snapToGrid; set => SetField(ref _snapToGrid, value); }

    public double GridSize { get => _gridSize; set => SetField(ref _gridSize, value); }

    public event EventHandler? Invalidated;

    /// <summary>A short message for the status bar (what was created, why nothing happened, ...).</summary>
    public event Action<string>? StatusMessage;

    /// <summary>Which kind of dimension the dimension tool places next.</summary>
    public DimensionKind CurrentDimensionKind { get => _currentDimensionKind; set => SetField(ref _currentDimensionKind, value); }

    public EditorContext()
    {
        SelectedShapes.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(SelectedShape));
    }

    private bool _refreshingConnections;

    public void RequestRedraw()
    {
        // Lines glued to a shape's connection points follow it whenever anything changes.
        if (!_refreshingConnections)
        {
            _refreshingConnections = true;
            try
            {
                ConnectionRouter.Refresh(Document);
                DimensionRouter.Refresh(Document);
            }
            finally
            {
                _refreshingConnections = false;
            }
        }

        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        UndoManager.Undo();
        RequestRedraw();
    }

    public void Redo()
    {
        UndoManager.Redo();
        RequestRedraw();
    }

    /// <summary>Clones the current selection into <see cref="Clipboard"/>, then deletes the
    /// originals (skipping any that are locked — see <see cref="DeleteSelection"/>).</summary>
    public void CutSelection()
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        Clipboard.Clear();
        Clipboard.AddRange(SelectedShapes.Where(s => !s.Locked).Select(CloneShape));
        DeleteSelection();
    }

    /// <summary>Clones the current selection into the clipboard without touching the originals.</summary>
    public void CopySelection()
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        Clipboard.Clear();
        Clipboard.AddRange(SelectedShapes.Select(CloneShape));
    }

    /// <summary>Inserts fresh clones of the clipboard contents, offset slightly so a paste never
    /// sits exactly on top of the shapes it came from.</summary>
    public void PasteClipboard()
    {
        if (Clipboard.Count == 0)
        {
            return;
        }

        var layer = SelectedLayer ?? Document.Layers.FirstOrDefault();
        if (layer is null)
        {
            return;
        }

        var pasted = Clipboard.Select(CloneShape).ToList();
        foreach (var shape in pasted)
        {
            shape.X += 12;
            shape.Y += 12;
        }

        var undoActions = new List<UndoableAction>();
        foreach (var shape in pasted)
        {
            layer.Shapes.Insert(0, shape);
            undoActions.Add(UndoableAction.ForInsert(layer.Shapes, shape, 0));
        }

        UndoManager.Push(UndoableAction.Combine(undoActions));

        SelectedShapes.Clear();
        foreach (var shape in pasted)
        {
            SelectedShapes.Add(shape);
        }

        SelectedLayer = layer;
        RequestRedraw();
    }

    /// <summary>Selects every shape of every visible, unlocked layer.</summary>
    public void SelectAll()
    {
        var all = Document.Layers.Where(l => l.Visible && !l.Locked).SelectMany(l => l.Shapes).ToList();
        SelectedShapes.Clear();
        foreach (var shape in all)
        {
            SelectedShapes.Add(shape);
        }

        RequestRedraw();
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        SelectedShapes.Clear();
        RequestRedraw();
    }

    /// <summary>Copies the selection and pastes it right away (offset a little), leaving the clipboard as it was.</summary>
    public void DuplicateSelection()
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        var previous = Clipboard.ToList();
        CopySelection();
        PasteClipboard();
        Clipboard.Clear();
        Clipboard.AddRange(previous);
    }

    /// <summary>Back to a pristine editing state after a document was replaced (new / opened): no history, no selection, the
    /// first layer active.</summary>
    public void ResetAfterDocumentReplaced()
    {
        UndoManager.Clear();
        SelectedShapes.Clear();
        SelectedLayer = Document.Layers.FirstOrDefault();
        ActiveTool = ActiveTool.Select;
        RequestRedraw();
    }

    /// <summary>Removes every selected shape that isn't locked (and isn't in a locked layer) —
    /// locked shapes stay selected so the user notices nothing happened and can unlock first.</summary>
    public void DeleteSelection()
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        var removals = new List<(ObservableCollection<Shape> Collection, Shape Shape, int Index)>();

        foreach (var shape in SelectedShapes.ToList())
        {
            foreach (var layer in Document.Layers)
            {
                if (shape.Locked || layer.Locked || !layer.Shapes.Contains(shape))
                {
                    continue;
                }

                var index = layer.Shapes.IndexOf(shape);
                removals.Add((layer.Shapes, shape, index));
                layer.Shapes.RemoveAt(index);
                break;
            }
        }

        if (removals.Count > 0)
        {
            UndoManager.Push(UndoableAction.ForRemove(removals));
        }

        foreach (var (_, shape, _) in removals)
        {
            SelectedShapes.Remove(shape);
        }

        RequestRedraw();
    }

    /// <summary>Assigns a fresh shared GroupId to every selected shape, so clicking any one of
    /// them afterward re-selects the whole group.</summary>
    public void GroupSelection()
    {
        if (SelectedShapes.Count < 2)
        {
            return;
        }

        var shapes = SelectedShapes.ToList();
        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        var groupId = Guid.NewGuid();
        foreach (var shape in shapes)
        {
            shape.GroupId = groupId;
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        RequestRedraw();
    }

    /// <summary>Clears GroupId on every currently selected shape.</summary>
    public void UngroupSelection()
    {
        var shapes = SelectedShapes.Where(s => s.GroupId is not null).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        foreach (var shape in shapes)
        {
            shape.GroupId = null;
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        RequestRedraw();
    }

    /// <summary>Sets <see cref="Shape.Locked"/> on every currently selected shape.</summary>
    public void SetSelectionLocked(bool locked)
    {
        if (SelectedShapes.Count == 0)
        {
            return;
        }

        var shapes = SelectedShapes.ToList();
        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        foreach (var shape in shapes)
        {
            shape.Locked = locked;
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        RequestRedraw();
    }

    /// <summary>Moves every selected (unlocked) shape to the front (index 0) of its own layer.</summary>
    public void BringSelectionToFront() => ReorderSelection(toFront: true);

    /// <summary>Moves every selected (unlocked) shape to the back (last index) of its own layer.</summary>
    public void SendSelectionToBack() => ReorderSelection(toFront: false);

    private void ReorderSelection(bool toFront)
    {
        var moveActions = new List<UndoableAction>();

        foreach (var shape in SelectedShapes.ToList())
        {
            if (shape.Locked)
            {
                continue;
            }

            var layer = Document.Layers.FirstOrDefault(l => !l.Locked && l.Shapes.Contains(shape));
            if (layer is null)
            {
                continue;
            }

            var fromIndex = layer.Shapes.IndexOf(shape);
            var toIndex = toFront ? 0 : layer.Shapes.Count - 1;
            if (fromIndex == toIndex)
            {
                continue;
            }

            layer.Shapes.Move(fromIndex, toIndex);
            moveActions.Add(UndoableAction.ForMove(layer.Shapes, fromIndex, toIndex));
        }

        if (moveActions.Count > 0)
        {
            UndoManager.Push(UndoableAction.Combine(moveActions));
            RequestRedraw();
        }
    }

    /// <summary>Replaces a shape by other shapes at the same place in its layer (a wall cut into pieces), as ONE undo step,
    /// and selects the new shapes.</summary>
    public void ReplaceShape(Shape original, IReadOnlyList<Shape> parts)
    {
        var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(original));
        if (layer is null || parts.Count == 0)
        {
            return;
        }

        var index = layer.Shapes.IndexOf(original);
        var actions = new List<UndoableAction>();
        layer.Shapes.RemoveAt(index);
        actions.Add(UndoableAction.ForRemove([(layer.Shapes, original, index)]));

        for (var i = 0; i < parts.Count; i++)
        {
            layer.Shapes.Insert(index + i, parts[i]);
            actions.Add(UndoableAction.ForInsert(layer.Shapes, parts[i], index + i));
        }

        UndoManager.Push(UndoableAction.Combine(actions));

        SelectedShapes.Clear();
        foreach (var part in parts)
        {
            SelectedShapes.Add(part);
        }

        RequestRedraw();
    }

    /// <summary>Creates the dimensions of every selected shape (width, height, diameter, angles, lengths ...) next to it, as ONE
    /// undo step. They stay attached: they follow the shape when it changes.</summary>
    public void AddDimensionsToSelection(DimensionSet set)
    {
        var created = 0;
        var actions = new List<UndoableAction>();

        foreach (var target in SelectedShapes.ToList())
        {
            var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(target));
            if (layer is null)
            {
                continue;
            }

            foreach (var dim in DimensionBuilder.Create(target, set))
            {
                layer.Shapes.Insert(0, dim);
                actions.Add(UndoableAction.ForInsert(layer.Shapes, dim, 0));
                created++;
            }
        }

        if (created == 0)
        {
            StatusMessage?.Invoke(SelectedShapes.Count == 0
                ? "Select a shape first to add its dimensions."
                : "This shape has no dimensions of that kind (walls show their own lengths).");
            return;
        }

        UndoManager.Push(UndoableAction.Combine(actions));
        StatusMessage?.Invoke(created == 1 ? "1 dimension added." : $"{created} dimensions added.");
        RequestRedraw();
    }

    /// <summary>Breaks every selected wall into one wall per segment.</summary>
    public void SplitSelectedWallsAtAllPoints()
    {
        foreach (var wall in SelectedShapes.Where(s => s.Kind == ShapeKind.Wall && !s.Locked && s.Points is { Count: > 1 }).ToList())
        {
            var parts = WallEditor.SplitAtAllPoints(wall);
            if (parts.Count > 1)
            {
                ReplaceShape(wall, parts);
            }
        }
    }

    private static Shape CloneShape(Shape source)
    {
        var clone = new Shape { Kind = source.Kind };
        ShapeSnapshot.Capture(source).ApplyTo(clone);
        return clone;
    }

    /// <summary>Moves every selected (unlocked) shape by a fixed offset — the Arrow-key nudge
    /// (see MainWindow's PreviewKeyDown), one undo entry per key press.</summary>
    public void NudgeSelection(double dx, double dy)
    {
        var shapes = SelectedShapes.Where(s => !s.Locked).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        foreach (var shape in shapes)
        {
            shape.X += dx;
            shape.Y += dy;
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        RequestRedraw();
    }

    /// <summary>Adds a mirrored, fading copy directly below each selected (unlocked) shape — a
    /// classic "water reflection" effect. FadeAmount already fades solid-top to transparent-bottom
    /// in plain document space regardless of FlipVertical, so the reflection reads as attached at
    /// the original's bottom edge and fading away below it with no extra rendering logic needed.</summary>
    public void ReflectSelection()
    {
        var shapes = SelectedShapes.Where(s => !s.Locked).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        var reflections = new List<Shape>();
        var undoActions = new List<UndoableAction>();

        foreach (var shape in shapes)
        {
            var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
            if (layer is null || layer.Locked)
            {
                continue;
            }

            var reflection = CloneShape(shape);
            reflection.FlipVertical = !shape.FlipVertical;
            reflection.Y = shape.Y + shape.Height;
            reflection.FadeAmount = Math.Max(shape.FadeAmount, 55);
            reflection.GroupId = null;

            layer.Shapes.Insert(0, reflection);
            undoActions.Add(UndoableAction.ForInsert(layer.Shapes, reflection, 0));
            reflections.Add(reflection);
        }

        if (reflections.Count == 0)
        {
            return;
        }

        UndoManager.Push(UndoableAction.Combine(undoActions));

        SelectedShapes.Clear();
        foreach (var reflection in reflections)
        {
            SelectedShapes.Add(reflection);
        }

        RequestRedraw();
    }

    /// <summary>Adds a border-offset copy of each selected (unlocked) shape — see
    /// <see cref="PathOffsetter.Offset"/>. <paramref name="distance"/> is signed: positive grows a
    /// closed shape outward / thickens an open line's outline, negative shrinks a closed shape
    /// inward (clamped so it can't collapse past nothing).</summary>
    public void OffsetSelection(double distance)
    {
        var shapes = SelectedShapes.Where(s => !s.Locked).ToList();
        if (shapes.Count == 0 || distance == 0)
        {
            return;
        }

        var offsets = new List<Shape>();
        var undoActions = new List<UndoableAction>();

        foreach (var shape in shapes)
        {
            var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
            if (layer is null || layer.Locked)
            {
                continue;
            }

            var offsetShape = PathOffsetter.Offset(shape, distance);
            if (offsetShape is null)
            {
                continue;
            }

            layer.Shapes.Insert(0, offsetShape);
            undoActions.Add(UndoableAction.ForInsert(layer.Shapes, offsetShape, 0));
            offsets.Add(offsetShape);
        }

        if (offsets.Count == 0)
        {
            return;
        }

        UndoManager.Push(UndoableAction.Combine(undoActions));

        SelectedShapes.Clear();
        foreach (var offsetShape in offsets)
        {
            SelectedShapes.Add(offsetShape);
        }

        RequestRedraw();
    }

    /// <summary>Merges 2+ selected shapes (all in the same layer) into one combined shape, using
    /// the union of their outlines (overlap included).</summary>
    public void UnionSelection()
    {
        var shapes = SelectedShapes.ToList();
        if (shapes.Count < 2 || shapes.Any(s => s.Locked))
        {
            return;
        }

        var layer = Document.Layers.FirstOrDefault(l => shapes.All(l.Shapes.Contains));
        if (layer is null)
        {
            return;
        }

        // Front-most first (index 0 = front-most) so the combined shape's colors come from
        // whichever shape is visually on top.
        var ordered = shapes.OrderBy(s => layer.Shapes.IndexOf(s)).ToList();
        var combined = ShapeCombiner.Union(ordered);
        if (combined is not null)
        {
            ReplaceWithCombinedShape(layer, ordered, combined);
        }
    }

    /// <summary>Combines every selected shape's geometry into ONE object (see
    /// <see cref="ShapeCombiner.Merge"/>) — unlike <see cref="UnionSelection"/>, works for lines
    /// and stroke-only icons too, and never distorts a piece's own look (no boolean fill-merge).</summary>
    public void MergeSelection()
    {
        var shapes = SelectedShapes.ToList();
        if (shapes.Count < 2 || shapes.Any(s => s.Locked))
        {
            return;
        }

        var layer = Document.Layers.FirstOrDefault(l => shapes.All(l.Shapes.Contains));
        if (layer is null)
        {
            return;
        }

        var ordered = shapes.OrderBy(s => layer.Shapes.IndexOf(s)).ToList();
        var merged = ShapeCombiner.Merge(ordered);
        if (merged is not null)
        {
            ReplaceWithCombinedShape(layer, ordered, merged);
        }
    }

    /// <summary>Cuts away the part of the second selected shape ("target") covered by the first
    /// ("cutter") — selection order picks the roles, not automatic z-order, so the user controls
    /// which shape actually gets trimmed.</summary>
    public void TrimSelection()
    {
        var shapes = SelectedShapes.ToList();
        if (shapes.Count != 2 || shapes.Any(s => s.Locked))
        {
            return;
        }

        var layer = Document.Layers.FirstOrDefault(l => shapes.All(l.Shapes.Contains));
        if (layer is null)
        {
            return;
        }

        var front = shapes[0];
        var back = shapes[1];

        var trimmed = ShapeCombiner.Trim(front, back);
        if (trimmed is not null)
        {
            ReplaceWithCombinedShape(layer, [front, back], trimmed);
        }
    }

    private void ReplaceWithCombinedShape(Layer layer, IReadOnlyList<Shape> sources, Shape combined)
    {
        var removals = sources.Select(s => (layer.Shapes, s, layer.Shapes.IndexOf(s))).ToList();
        foreach (var (collection, shape, _) in removals)
        {
            collection.Remove(shape);
        }

        layer.Shapes.Insert(0, combined);

        UndoManager.Push(UndoableAction.Combine(
            [UndoableAction.ForRemove(removals), UndoableAction.ForInsert(layer.Shapes, combined, 0)]));

        SelectedShapes.Clear();
        SelectedShapes.Add(combined);
        SelectedLayer = layer;
        RequestRedraw();
    }

    /// <summary>Converts a fixed-geometry shape (Star, Cross, etc.) into a freeform points-based
    /// shape so every vertex becomes independently draggable via the existing point-edit handles.
    /// No-op if the kind doesn't support it (see <see cref="ShapeGeometryFactory.SupportsPointEditing"/>)
    /// or it's already points-based.</summary>
    /// <summary>Replaces a Text shape with an outlined <see cref="ShapeKind.CombinedPath"/> (see
    /// <see cref="TextOutliner.ConvertToShape"/>) — <see cref="Shape.Kind"/> is <c>init</c>-only,
    /// so this can never be an in-place mutation like <see cref="ConvertToEditablePoints"/>.</summary>
    public void ConvertTextToShape(Shape shape)
    {
        if (shape.Locked || shape.Kind != ShapeKind.Text)
        {
            return;
        }

        var layer = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
        if (layer is null || layer.Locked)
        {
            return;
        }

        var converted = TextOutliner.ConvertToShape(shape);
        if (converted is not null)
        {
            ReplaceWithCombinedShape(layer, [shape], converted);
        }
    }

    public void ConvertToEditablePoints(Shape shape)
    {
        if (shape.Locked || shape.Points is not null || !ShapeGeometryFactory.SupportsPointEditing(shape.Kind))
        {
            return;
        }

        var before = ShapeSnapshot.Capture(shape);

        // Already shape-local (origin = this shape's own X,Y) — ExtractEditablePoints mirrors
        // BuildLocal's un-offset local template, matching Shape.Points' documented convention.
        shape.Points = ShapeGeometryFactory.ExtractEditablePoints(shape);

        var after = ShapeSnapshot.Capture(shape);
        UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [after]));
        RequestRedraw();
    }
}
