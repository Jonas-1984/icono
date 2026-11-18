using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>The settings of the Pen tool (handwriting), shown while it is the tool: colour, width on the paper and smoothing of the tremor.</summary>
public sealed partial class PropertiesPanel
{
    private void BuildPen(EditorContext context)
    {
        Title("Pen");
        Note("Write or draw with the pen or a finger. Every stroke is one object you can move, recolour, delete or undo. " +
             "Two fingers move and zoom the page.");

        Heading("SHAPE CORRECTION");
        Toggle("Hold to make shapes clean", context.PenSnapShapes, on => context.PenSnapShapes = on);
        Note("Draw a circle, line, rectangle or triangle and keep the pen or finger still for a moment: it becomes the clean shape. Lift to keep it, or move on to go on drawing freehand.");

        Heading("ERASER");
        Toggle("Erase strokes (drag over them)", context.PenEraser, on => context.PenEraser = on);
        Note(context.PenEraser
            ? "Touching a stroke deletes it. Only handwriting is erased - never walls, furniture or text. Undo brings a stroke back."
            : "Off: the pen writes. A pen with an eraser end erases with that end.");

        Heading("COLOUR");
        ColorRow(context.PenColor, color => context.PenColor = color);

        Heading("WIDTH");
        NumberRow("Width", context.PenWidth / PxPerCm * 10, "mm", 0.1, 0.2, 15, "0.0", mm => context.PenWidth = mm / 10 * PxPerCm);

        Heading("SMOOTHING");
        NumberRow("Smoothing", context.PenSmoothing, "", 1, 0, 10, "0", value => context.PenSmoothing = value);
        Note("0 = exactly as written; higher values take the tremor out of the stroke.");
    }
}
