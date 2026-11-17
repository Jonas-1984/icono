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

        Heading("COLOUR");
        ColorRow(context.PenColor, color => context.PenColor = color);

        Heading("WIDTH");
        NumberRow("Width", context.PenWidth / PxPerCm * 10, "mm", 0.1, 0.2, 15, "0.0", mm => context.PenWidth = mm / 10 * PxPerCm);

        Heading("SMOOTHING");
        NumberRow("Smoothing", context.PenSmoothing, "", 1, 0, 10, "0", value => context.PenSmoothing = value);
        Note("0 = exactly as written; higher values take the tremor out of the stroke.");
    }
}
