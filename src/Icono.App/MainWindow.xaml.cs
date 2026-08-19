using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Icono.App.State;
using Icono.Core;

namespace Icono.App;

public partial class MainWindow : Window
{
    private readonly EditorContext _context = new();

    public MainWindow()
    {
        InitializeComponent();

        _context.Document.Layers.Add(new Layer { Name = "Layer 1" });
        _context.SelectedLayer = _context.Document.Layers[0];

        ToolPanel.Context = _context;
        CanvasHost.Context = _context;
        PropertiesPanel.Context = _context;

        StateChanged += MainWindow_OnStateChanged;
        Closing += MainWindow_OnClosing;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
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

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Are you sure you want to close the app?",
            "icono",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBox)
        {
            // A layer name, size, or hex field is being edited — let Delete edit text, not shapes.
            return;
        }

        if (_context.SelectedShapes.Count == 0)
        {
            return;
        }

        foreach (var shape in _context.SelectedShapes.ToList())
        {
            foreach (var layer in _context.Document.Layers)
            {
                layer.Shapes.Remove(shape);
            }
        }

        _context.SelectedShapes.Clear();
        _context.RequestRedraw();
        e.Handled = true;
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
