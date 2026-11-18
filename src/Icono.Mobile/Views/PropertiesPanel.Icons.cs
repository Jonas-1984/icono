using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>"Save as icon": the selection (with everything that is grouped with it) becomes an icon of MY ICONS, to be put into any drawing later
/// (<see cref="IconLibrary.SaveSelection"/>, shared with the Windows program). It is found in Objects, group My icons.</summary>
public sealed partial class PropertiesPanel
{
    private static void SaveAsIcon(EditorContext context)
    {
        try
        {
            var entry = IconLibrary.SaveSelection(context, MyIcons.Store);
            context.Notify(entry is null
                ? "Select something first."
                : $"Saved as \"{entry.Name}\" - find it in Objects, My icons (hold it to remove it).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Notify("The icon could not be saved: " + ex.Message);
        }
    }
}
