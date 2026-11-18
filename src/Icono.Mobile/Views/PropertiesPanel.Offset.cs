using System.Globalization;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>"Offset...": a copy of the selected objects, parallel to them at a distance - bigger (positive) or smaller (negative), see
/// <see cref="EditorContext.OffsetSelection"/>. The distance is asked for like a length (real size at the drawing scale).</summary>
public sealed partial class PropertiesPanel
{
    /// <summary>Raised when the panel needs a text typed (the window shows the text box and hands the answer to the request).</summary>
    public event EventHandler<LengthRequest>? TextBoxRequested;

    private void AskOffset(EditorContext context)
    {
        TextBoxRequested?.Invoke(this, new LengthRequest(
            "Offset",
            $"Distance in {UnitName} (real size) of the parallel copy. Positive makes it bigger, negative smaller.",
            UnitFactor >= 1 ? (10 * UnitFactor).ToString("0.##", CultureInfo.InvariantCulture) : (10 * UnitFactor).ToString("0.###", CultureInfo.InvariantCulture),
            text =>
            {
                if (text is null || !double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cm) || cm == 0)
                {
                    context.Notify("That is not a distance. Type a number, for example 10 or -10.");
                    return;
                }

                var first = context.SelectedShapes.FirstOrDefault();
                context.OffsetSelection(FromReal(cm));
                if (ReferenceEquals(first, context.SelectedShapes.FirstOrDefault()))
                {
                    context.Notify("Nothing to offset: unlock the objects and their layer.");
                }
            }));
    }
}
