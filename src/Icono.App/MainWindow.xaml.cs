using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Icono.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        StateChanged += MainWindow_OnStateChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.EnableDarkTitleBar(hwnd);
        NativeMethods.EnableRoundedCorners(hwnd);

        if (NativeMethods.TryEnableMicaBackdrop(hwnd))
        {
            // Mostly-opaque anthracite tint: only a small fraction of the Mica material shows
            // through ("very little blur"), and it still looks like today's anthracite theme
            // if the OS/session falls back to a flat color instead of real Mica (e.g. over RDP
            // or in some virtualized/sandboxed sessions where DWM composition effects are off).
            var micaTint = Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B);
            Background = new SolidColorBrush(micaTint);
            RootChrome.Background = new SolidColorBrush(micaTint);
        }
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        RootChrome.CornerRadius = new CornerRadius(isMaximized ? 0 : 8);
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "icono\nWindows Icon Editor\n\nMilestone 1 — Foundation Scaffold",
            "About icono",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
