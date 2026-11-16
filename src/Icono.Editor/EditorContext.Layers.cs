using System.Collections.ObjectModel;
using Icono.Core;
using Icono.Editor.Undo;

namespace Icono.Editor;

/// <summary>Layers: add, rename, order, delete, show / lock, and move the selection to another layer - shared by the Windows program and the
/// tablet app. Index 0 of <see cref="Document.Layers"/> is the front-most layer. Every change of the drawing is one undo step; showing / hiding
/// and locking are view settings, as on Windows, and not undone.</summary>
public sealed partial class EditorContext
{
    /// <summary>Raised when layers were added, removed, renamed or reordered (the lists of layers rebuild).</summary>
    public event Action? LayersChanged;

    /// <summary>Puts a new, empty layer in front of all others and makes it the active one.</summary>
    public Layer AddLayer()
    {
        var number = Document.Layers.Count + 1;
        while (Document.Layers.Any(l => l.Name == $"Layer {number}"))
        {
            number++;
        }

        var layer = new Layer { Name = $"Layer {number}" };
        Document.Layers.Insert(0, layer);
        UndoManager.Push(UndoableAction.ForLayerInsert(Document.Layers, layer, 0));
        SelectedLayer = layer;
        LayersChanged?.Invoke();
        RequestRedraw();
        return layer;
    }

    /// <summary>Moves a layer <paramref name="delta"/> places to the front (negative) or the back (positive).</summary>
    public bool MoveLayer(Layer layer, int delta)
    {
        var index = Document.Layers.IndexOf(layer);
        if (index < 0)
        {
            return false;
        }

        var newIndex = Math.Clamp(index + delta, 0, Document.Layers.Count - 1);
        if (newIndex == index)
        {
            return false;
        }

        Document.Layers.Move(index, newIndex);
        UndoManager.Push(UndoableAction.ForMove(Document.Layers, index, newIndex));
        LayersChanged?.Invoke();
        RequestRedraw();
        return true;
    }

    /// <summary>Gives a layer a new name (an empty name is ignored).</summary>
    public bool RenameLayer(Layer layer, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name == layer.Name)
        {
            return false;
        }

        var before = layer.Name;
        void Apply(string value)
        {
            layer.Name = value;
            LayersChanged?.Invoke();
        }

        Apply(name);
        UndoManager.Push(UndoableAction.ForValue(() => Apply(before), () => Apply(name)));
        return true;
    }

    /// <summary>Removes a layer with everything on it (the last layer stays). One undo step brings both back.</summary>
    public bool DeleteLayer(Layer layer)
    {
        var index = Document.Layers.IndexOf(layer);
        if (index < 0 || Document.Layers.Count <= 1)
        {
            StatusMessage?.Invoke("A drawing needs at least one layer.");
            return false;
        }

        foreach (var shape in layer.Shapes)
        {
            SelectedShapes.Remove(shape);
        }

        Document.Layers.RemoveAt(index);
        void Restore()
        {
            Document.Layers.Insert(Math.Min(index, Document.Layers.Count), layer);
            LayersChanged?.Invoke();
        }

        void Remove()
        {
            Document.Layers.Remove(layer);
            if (ReferenceEquals(SelectedLayer, layer))
            {
                SelectedLayer = Document.Layers.FirstOrDefault();
            }

            LayersChanged?.Invoke();
        }

        UndoManager.Push(UndoableAction.ForValue(Restore, Remove));
        if (ReferenceEquals(SelectedLayer, layer))
        {
            SelectedLayer = Document.Layers[Math.Min(index, Document.Layers.Count - 1)];
        }

        LayersChanged?.Invoke();
        RequestRedraw();
        return true;
    }

    /// <summary>Shows or hides a layer (a hidden layer is neither drawn nor picked).</summary>
    public void SetLayerVisible(Layer layer, bool visible)
    {
        layer.Visible = visible;
        if (!visible)
        {
            foreach (var shape in layer.Shapes)
            {
                SelectedShapes.Remove(shape);
            }
        }

        LayersChanged?.Invoke();
        RequestRedraw();
    }

    /// <summary>Locks or unlocks a layer (the objects of a locked layer cannot be moved or changed).</summary>
    public void SetLayerLocked(Layer layer, bool locked)
    {
        layer.Locked = locked;
        LayersChanged?.Invoke();
        RequestRedraw();
    }

    /// <summary>Moves the selected objects (the unlocked ones) to the front of <paramref name="target"/>, as one undo step.</summary>
    public bool MoveSelectionToLayer(Layer target)
    {
        var moves = new List<UndoableAction>();
        foreach (var shape in SelectedShapes.Where(s => !s.Locked).ToList())
        {
            var source = Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
            if (source is null || ReferenceEquals(source, target) || source.Locked)
            {
                continue;
            }

            var index = source.Shapes.IndexOf(shape);
            source.Shapes.RemoveAt(index);
            target.Shapes.Insert(0, shape);
            moves.Add(UndoableAction.ForRemove([(source.Shapes, shape, index)]));
            moves.Add(UndoableAction.ForInsert(target.Shapes, shape, 0));
        }

        if (moves.Count == 0)
        {
            return false;
        }

        UndoManager.Push(UndoableAction.Combine(moves));
        SelectedLayer = target;
        LayersChanged?.Invoke();
        RequestRedraw();
        return true;
    }
}
