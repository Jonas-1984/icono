using System.Collections.ObjectModel;
using Icono.Core;

namespace Icono.App.State.Undo;

/// <summary>
/// One flexible <see cref="IUndoableAction"/> driven by Undo/Redo delegates, plus static
/// factories covering every mutation shape this app performs (property edits, insert, remove,
/// reorder). Avoids a bespoke subclass per gesture type.
/// </summary>
public sealed class UndoableAction : IUndoableAction
{
    private readonly Action _undo;
    private readonly Action _redo;

    private UndoableAction(Action undo, Action redo)
    {
        _undo = undo;
        _redo = redo;
    }

    public void Undo() => _undo();

    public void Redo() => _redo();

    /// <summary>Reverts/reapplies a batch of shapes to their captured before/after state.</summary>
    public static UndoableAction ForPropertyEdit(
        IReadOnlyList<Shape> shapes, IReadOnlyList<ShapeSnapshot> before, IReadOnlyList<ShapeSnapshot> after)
    {
        return new UndoableAction(
            undo: () =>
            {
                for (var i = 0; i < shapes.Count; i++)
                {
                    before[i].ApplyTo(shapes[i]);
                }
            },
            redo: () =>
            {
                for (var i = 0; i < shapes.Count; i++)
                {
                    after[i].ApplyTo(shapes[i]);
                }
            });
    }

    public static UndoableAction ForInsert(ObservableCollection<Shape> collection, Shape shape, int index)
    {
        return new UndoableAction(
            undo: () => collection.Remove(shape),
            redo: () => collection.Insert(index, shape));
    }

    /// <summary>Removes one or more shapes (e.g. a multi-selection Delete) as a single gesture.</summary>
    public static UndoableAction ForRemove(IReadOnlyList<(ObservableCollection<Shape> Collection, Shape Shape, int Index)> removals)
    {
        return new UndoableAction(
            undo: () =>
            {
                foreach (var (collection, shape, index) in removals)
                {
                    collection.Insert(Math.Min(index, collection.Count), shape);
                }
            },
            redo: () =>
            {
                foreach (var (collection, shape, _) in removals)
                {
                    collection.Remove(shape);
                }
            });
    }

    /// <summary>Reorders any ObservableCollection (Layers front/back, or a Layer's Shapes
    /// front/back — index 0 is front-most in both).</summary>
    public static UndoableAction ForMove<T>(ObservableCollection<T> collection, int fromIndex, int toIndex)
    {
        return new UndoableAction(
            undo: () => collection.Move(toIndex, fromIndex),
            redo: () => collection.Move(fromIndex, toIndex));
    }

    public static UndoableAction ForLayerInsert(ObservableCollection<Layer> collection, Layer layer, int index)
    {
        return new UndoableAction(
            undo: () => collection.Remove(layer),
            redo: () => collection.Insert(index, layer));
    }

    /// <summary>Bundles several already-applied actions into one undo/redo step (e.g. inserting
    /// N shapes from a library entry as a single Ctrl+Z). Undoes in reverse (LIFO) order.</summary>
    public static UndoableAction Combine(IReadOnlyList<UndoableAction> actions)
    {
        return new UndoableAction(
            undo: () =>
            {
                for (var i = actions.Count - 1; i >= 0; i--)
                {
                    actions[i].Undo();
                }
            },
            redo: () =>
            {
                foreach (var action in actions)
                {
                    action.Redo();
                }
            });
    }
}
