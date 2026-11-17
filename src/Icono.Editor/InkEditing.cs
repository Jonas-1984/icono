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
}
