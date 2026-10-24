using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Icono.App.Views;

/// <summary>
/// Base class of every dialog of the app: a borderless, transparent window that hosts a <see cref="DialogCard"/>, so all
/// dialogs share the glass/neumorphic look of the main window (instead of the grey system chrome). Esc cancels.
/// </summary>
public class ThemedDialogWindow : Window
{
    public ThemedDialogWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                CancelDialog();
            }
        };
    }

    /// <summary>Closes the dialog as cancelled (works for modal and modeless use).</summary>
    public void CancelDialog()
    {
        try
        {
            DialogResult = false;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}
