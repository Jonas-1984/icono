using System.Windows;

namespace Icono.App.Views;

/// <summary>Help > Keyboard Shortcuts: every shortcut of the editor in one list.</summary>
public partial class ShortcutsDialog : ThemedDialogWindow
{
    public sealed record Shortcut(string Action, string Keys);

    private static readonly Shortcut[] Shortcuts =
    [
        new("New document", "Ctrl+N"),
        new("Open document", "Ctrl+O"),
        new("Save / Save as", "Ctrl+S / Ctrl+Shift+S"),
        new("Export", "Ctrl+E"),
        new("Undo / Redo", "Ctrl+Z / Ctrl+Y"),
        new("Cut / Copy / Paste", "Ctrl+X / Ctrl+C / Ctrl+V"),
        new("Duplicate", "Ctrl+D"),
        new("Delete selection", "Del"),
        new("Select all / Deselect", "Ctrl+A / Esc"),
        new("Group / Ungroup", "Ctrl+G / Ctrl+Shift+G"),
        new("Move selection (10 px with Shift)", "Arrow keys"),
        new("Zoom in / out", "Ctrl++ / Ctrl+-"),
        new("Actual size / fit page", "Ctrl+0 / Ctrl+1"),
        new("Selection / Pan / Eyedropper / Bucket", "V / H / I / G"),
        new("Text tool", "T"),
        new("Wall tool / Cut wall", "W / X"),
        new("Dimension tool (click the points, then where the line goes)", "D"),
        new("Finish / cancel a polyline or curve", "Enter / Esc"),
        new("Resize keeping proportions", "Drag a corner handle"),
        new("Resize freely", "Shift + drag a corner handle"),
        new("Clear a design-grid square", "Alt + click with the bucket"),
    ];

    public ShortcutsDialog()
    {
        InitializeComponent();
        ShortcutList.ItemsSource = Shortcuts;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
