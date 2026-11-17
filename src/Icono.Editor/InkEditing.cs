using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>Handwriting: turning the points of a pen (or finger) stroke into an object - shared by the Windows program and the tablet app. Every
/// stroke, from putting the pen down to lifting it, is one object in the pen colour and width of the <see cref="EditorContext"/>, one undo step.</summary>
public static class InkEditing
{
    /// <summary>The page distance the tremor is averaged over: the hand shakes by screen pixels, so the distance follows the zoom.</summary>
    public static float SmoothingDistance(EditorContext context, double zoom) => (float)(context.PenSmoothing * 0.75 / Math.Max(zoom, 1e-6));

    /// <summary>The stroke as it looks while it is being drawn (coarser than the final one, so that it stays fast), or null for too few points.</summary>
    public static Shape? Preview(EditorContext context, IReadOnlyList<SKPoint> points, double zoom) =>
        FreehandBuilder.CreateShape(points, context.PenColor, context.PenWidth, (float)(0.5 / Math.Max(zoom, 1e-6)), SmoothingDistance(context, zoom));

    /// <summary>The stroke becomes an object at the front of the active (or first unlocked) layer. False when nothing was added.</summary>
    public static bool Commit(EditorContext context, IReadOnlyList<SKPoint> points, double zoom)
    {
        var layer = context.SelectedLayer is { Locked: false } selected ? selected : context.Document.Layers.FirstOrDefault(l => !l.Locked);
        if (layer is null)
        {
            context.Notify("All layers are locked.");
            return false;
        }

        var shape = FreehandBuilder.CreateShape(
            points, context.PenColor, context.PenWidth, (float)Math.Max(0.2, 0.8 / Math.Max(zoom, 1e-6)), SmoothingDistance(context, zoom));
        if (shape is null)
        {
            return false;
        }

        layer.Shapes.Insert(0, shape);
        context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
        context.RequestRedraw();
        return true;
    }

    /// <summary>The eraser reaches this far (screen pixels) beside the middle of a stroke.</summary>
    public const double EraserReachPx = 10;

    /// <summary>Deletes the handwriting strokes the eraser touches at (<paramref name="x"/>, <paramref name="y"/>) and remembers them in
    /// <paramref name="erased"/> (shape, its layer and place in it). ONLY strokes of the pen are touched - never walls, furniture, texts or
    /// dimensions - and not those on locked layers or locked themselves. Returns how many were deleted by this touch.</summary>
    public static int EraseAt(EditorContext context, double x, double y, double zoom, List<(Layer Layer, Shape Shape, int Index)> erased)
    {
        var reach = EraserReachPx / Math.Max(zoom, 1e-6);
        var removed = 0;
        foreach (var layer in context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            for (var i = 0; i < layer.Shapes.Count; i++)
            {
                var shape = layer.Shapes[i];
                if (shape.Freehand && !shape.Locked && FreehandBuilder.HitTest(shape, x, y, reach))
                {
                    layer.Shapes.RemoveAt(i);
                    context.SelectedShapes.Remove(shape);
                    erased.Add((layer, shape, i));
                    removed++;
                    i--;
                }
            }
        }

        if (removed > 0)
        {
            context.RequestRedraw();
        }

        return removed;
    }

    /// <summary>Keeps what the eraser deleted as ONE undo step (the strokes are already gone from the drawing).</summary>
    public static void CommitErase(EditorContext context, IReadOnlyList<(Layer Layer, Shape Shape, int Index)> erased)
    {
        if (erased.Count == 0)
        {
            return;
        }

        // each place was noted at the moment its stroke was removed, so undo has to put them back in the opposite order
        context.UndoManager.Push(UndoableAction.ForRemove(erased.Reverse().Select(e => (e.Layer.Shapes, e.Shape, e.Index)).ToList()));
        context.RequestRedraw();
    }

    /// <summary>Puts back what the eraser deleted (the touch was only a second finger that wanted to zoom).</summary>
    public static void RestoreErased(EditorContext context, IReadOnlyList<(Layer Layer, Shape Shape, int Index)> erased)
    {
        // back to front, so every stroke returns to the place it had
        for (var i = erased.Count - 1; i >= 0; i--)
        {
            var (layer, shape, index) = erased[i];
            layer.Shapes.Insert(Math.Min(index, layer.Shapes.Count), shape);
        }

        context.RequestRedraw();
    }
}
