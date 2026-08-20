using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Icono.App.State;
using Icono.App.State.Undo;
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

        GridSizeTextBox.Text = _context.GridSize.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
        if (Keyboard.FocusedElement is TextBox)
        {
            // A layer name, size, or hex field is being edited — let these keys edit text, not shapes.
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelectedShapes();
            e.Handled = true;
            return;
        }

        if (_context.ActiveTool == ActiveTool.PathDraw)
        {
            if (e.Key == Key.Escape)
            {
                CanvasHost.CancelPathDraw();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                CanvasHost.FinishPathDraw();
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z)
        {
            _context.Undo();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Y)
        {
            _context.Redo();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.X)
        {
            CutSelectedShapes();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.C)
        {
            CopySelectedShapes();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.V)
        {
            PasteClipboard();
            e.Handled = true;
        }
    }

    private void DeleteSelectedShapes() => _context.DeleteSelection();

    private void CutSelectedShapes() => _context.CutSelection();

    private void CopySelectedShapes() => _context.CopySelection();

    private void PasteClipboard() => _context.PasteClipboard();

    private void UndoMenuItem_OnClick(object sender, RoutedEventArgs e) => _context.Undo();

    private void RedoMenuItem_OnClick(object sender, RoutedEventArgs e) => _context.Redo();

    private void CutButton_OnClick(object sender, RoutedEventArgs e) => CutSelectedShapes();

    private void CutMenuItem_OnClick(object sender, RoutedEventArgs e) => CutSelectedShapes();

    private void CopyMenuItem_OnClick(object sender, RoutedEventArgs e) => CopySelectedShapes();

    private void PasteMenuItem_OnClick(object sender, RoutedEventArgs e) => PasteClipboard();

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e) => DeleteSelectedShapes();

    private void TextButton_OnClick(object sender, RoutedEventArgs e)
    {
        _context.ActiveTool = ActiveTool.Shape;
        _context.SelectedShapeKind = ShapeKind.Text;
    }

    private void GridToggleButton_OnToggled(object sender, RoutedEventArgs e)
    {
        _context.ShowGrid = GridToggleButton.IsChecked == true;
        _context.RequestRedraw();
    }

    private void SnapToggleButton_OnToggled(object sender, RoutedEventArgs e)
    {
        _context.SnapToGrid = SnapToggleButton.IsChecked == true;
    }

    private void GridSizeTextBox_OnLostFocus(object sender, RoutedEventArgs e) => ApplyGridSizeTextBox();

    private void GridSizeTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyGridSizeTextBox();
        }
    }

    private void ApplyGridSizeTextBox()
    {
        if (!double.TryParse(GridSizeTextBox.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            GridSizeTextBox.Text = _context.GridSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        _context.GridSize = value;
        _context.RequestRedraw();
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
