using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Icono.App.State;
using Icono.App.State.Undo;
using Icono.App.Views;
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

        if (Keyboard.FocusedElement is ComboBox or Slider)
        {
            // Arrow keys are that control's own way to change its value/navigate its list — don't
            // steal them for shape-nudging while one of these has focus.
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelectedShapes();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right && _context.SelectedShapes.Count > 0)
        {
            // Only swallow the key when there's actually a selection to move — otherwise leave
            // arrow keys alone so RadioButton-group keyboard navigation (the tool/shape pickers)
            // keeps working as normal.
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10.0 : 1.0;
            var (dx, dy) = e.Key switch
            {
                Key.Up => (0.0, -step),
                Key.Down => (0.0, step),
                Key.Left => (-step, 0.0),
                _ => (step, 0.0),
            };

            _context.NudgeSelection(dx, dy);
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

    private void CopyButton_OnClick(object sender, RoutedEventArgs e) => CopySelectedShapes();

    private void PasteButton_OnClick(object sender, RoutedEventArgs e) => PasteClipboard();

    private void MergeButton_OnClick(object sender, RoutedEventArgs e) => _context.MergeSelection();

    private void GroupButton_OnClick(object sender, RoutedEventArgs e) => _context.GroupSelection();

    private void UngroupButton_OnClick(object sender, RoutedEventArgs e) => _context.UngroupSelection();

    private void LockButton_OnClick(object sender, RoutedEventArgs e) => _context.SetSelectionLocked(true);

    private void UnlockButton_OnClick(object sender, RoutedEventArgs e) => _context.SetSelectionLocked(false);

    private void BringToFrontButton_OnClick(object sender, RoutedEventArgs e) => _context.BringSelectionToFront();

    private void SendToBackButton_OnClick(object sender, RoutedEventArgs e) => _context.SendSelectionToBack();

    private void UnionButton_OnClick(object sender, RoutedEventArgs e) => _context.UnionSelection();

    private void TrimButton_OnClick(object sender, RoutedEventArgs e) => _context.TrimSelection();

    private void ReflectButton_OnClick(object sender, RoutedEventArgs e) => _context.ReflectSelection();

    private void OffsetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OffsetDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _context.OffsetSelection(dialog.Distance);
        }
    }

    private void RegionColorToolRadioButton_OnChecked(object sender, RoutedEventArgs e) =>
        _context.ActiveTool = ActiveTool.RegionColor;

    private void BorderToolRadioButton_OnChecked(object sender, RoutedEventArgs e)
    {
        // The Border tool's exact meaning (frame-around-canvas vs. a shape kind) is ambiguous —
        // treated as a Select alias for now, pending clarification.
        _context.ActiveTool = ActiveTool.Select;
    }

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
