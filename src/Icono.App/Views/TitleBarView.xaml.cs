using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace Icono.App.Views;

public partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
        Loaded += TitleBarView_OnLoaded;
    }

    /// <summary>Shows the document name (with a * for unsaved changes) in the title row.</summary>
    public void SetDocumentTitle(string text) => TitleText.Text = text;

    /// <summary>Hooks the owning Window's StateChanged so the Maximize button swaps to the
    /// "Restore" glyph (and back) exactly like the OS-drawn caption buttons would.</summary>
    private void TitleBarView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window)
        {
            return;
        }

        window.StateChanged += (_, _) => UpdateMaximizeRestoreIcon(window.WindowState);
        UpdateMaximizeRestoreIcon(window.WindowState);
    }

    private void UpdateMaximizeRestoreIcon(WindowState state)
    {
        var isMaximized = state == WindowState.Maximized;
        MaximizeRestoreIcon.Data = (Geometry)FindResource(isMaximized ? "CaptionRestoreGeometry" : "CaptionMaximizeGeometry");
        MaximizeRestoreButton.ToolTip = isMaximized ? "Restore Down" : "Maximize";
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            SystemCommands.MinimizeWindow(window);
        }
    }

    private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(window);
        }
        else
        {
            SystemCommands.MaximizeWindow(window);
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            SystemCommands.CloseWindow(window);
        }
    }
}
