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

        // A new session starts on an empty, white DIN A4 page.
        _context.Document.Width = Document.A4Width;
        _context.Document.Height = Document.A4Height;
        _context.Document.BackgroundMode = BackgroundMode.SolidColor;
        _context.Document.BackgroundColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);

        _context.Document.Layers.Add(new Layer { Name = "Layer 1" });
        _context.SelectedLayer = _context.Document.Layers[0];

        _context.StatusMessage += message => StatusBar.ShowMessage(message);
        _context.ScaleChanged += denominator =>
        {
            StatusBar.SetScale(denominator);
            UpdateScaleMenuMarkers();
        };
        _context.SheetChanged += () => SheetMenuItem.IsChecked = _context.Document.Sheet.Enabled;
        _context.ObjectSnap.PropertyChanged += (_, _) => ObjectSnapMenuItem.IsChecked = _context.ObjectSnap.Enabled;

        // A new page size (preset, portrait / landscape, typed size) is shown whole.
        var shownPage = (_context.Document.Width, _context.Document.Height);
        _context.Invalidated += (_, _) =>
        {
            var page = (_context.Document.Width, _context.Document.Height);
            if (page != shownPage)
            {
                shownPage = page;
                UpdatePageUi();
                FitPageToWindow();
            }
        };
        ToolPanel.Context = _context;
        CanvasHost.Context = _context;
        PropertiesPanel.Context = _context;

        GridSizeTextBox.Text = _context.GridSize.ToString(System.Globalization.CultureInfo.InvariantCulture);

        StateChanged += MainWindow_OnStateChanged;
        Closing += MainWindow_OnClosing;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        // A click anywhere outside the text editor (panels, menus, toolbar) keeps what was typed.
        PreviewMouseDown += (_, e) => CanvasHost.CommitTextEditUnlessInside(e.OriginalSource);
        PreviewMouseWheel += MainWindow_OnPreviewMouseWheel;
        Loaded += (_, _) => FitPageToWindow();

        UpdateThemeMenuMarkers();
        InitializeDocumentTracking();
        BuildScaleMenu();
        StatusBar.SetScale(_context.Document.ScaleDenominator);
        SyncToolUi();
        UpdatePageUi();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.EnableDarkTitleBar(hwnd);
        NativeMethods.EnableRoundedCorners(hwnd);

        // The window paints its own opaque graphite gradient, so Mica is only requested for the OS material.
        NativeMethods.TryEnableMicaBackdrop(hwnd);
    }

    /// <summary>Zooms so the whole page is visible in the work area (with a little breathing room).</summary>
    private void FitPageToWindow()
    {
        if (CanvasHost.ActualWidth <= 0 || CanvasHost.ActualHeight <= 0)
        {
            return;
        }

        const double margin = 56;
        var zoom = Math.Min(
            (CanvasHost.ActualWidth - margin) / _context.Document.Width,
            (CanvasHost.ActualHeight - margin) / _context.Document.Height);
        StatusBar.SetZoom(Math.Floor(zoom * 100) / 100);
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        RootChrome.CornerRadius = new CornerRadius(isMaximized ? 0 : 12);

        // A maximized borderless window overhangs the screen by its resize frame: pull the content back in so no edge or corner is cut off.
        RootChrome.Margin = isMaximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        NativeMethods.SetCornerPreference(new WindowInteropHelper(this).Handle, round: !isMaximized);
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        // Only ask when there is something to lose: offer to save unsaved changes.
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
        }
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.S or Key.E or Key.N or Key.O or Key.P)
        {
            CanvasHost.CommitActiveTextEdit();
        }

        // File shortcuts (Ctrl+N/O/S/E) work everywhere, even while a text field has the focus.
        if (HandleFileShortcuts(e))
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox)
        {
            // A layer name, size, or hex field is being edited — let these keys edit text, not shapes.
            return;
        }

        if (Keyboard.FocusedElement is ComboBox or Slider && e.Key != Key.Delete)
        {
            // Arrow keys are that control's own way to change its value/navigate its list — don't
            // steal them for shape-nudging while one of these has focus. Delete is not used by either
            // control, so it still removes the selected drawing (e.g. right after dragging a slider).
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

        if (HandleEditorShortcuts(e))
        {
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
                _context.ActiveTool = ActiveTool.Select;
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

    /// <summary>Ctrl + mouse wheel over the work area zooms the page in / out.</summary>
    private void MainWindow_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !CanvasHost.IsMouseOver || e.Delta == 0)
        {
            return;
        }

        StatusBar.ZoomBy(e.Delta > 0 ? 1.1 : 1 / 1.1);
        e.Handled = true;
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

    private void BorderToolRadioButton_OnChecked(object sender, RoutedEventArgs e)
    {
        // The toolbar's Border key is a shortcut to the BORDER card (thickness, colour, style, corners) of the selected shape.
        _context.ActiveTool = ActiveTool.Select;
        BorderToolbarRadio.IsChecked = false;
        if (!PropertiesPanel.FocusCard("BORDER"))
        {
            ThemedMessageBox.Show(this, "Select a shape first to edit its border.", "Border", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void TextButton_OnClick(object sender, RoutedEventArgs e)
    {
        _context.ActiveTool = ActiveTool.Shape;
        _context.SelectedShapeKind = ShapeKind.Text;
    }

    private void GridToggleButton_OnToggled(object sender, RoutedEventArgs e)
    {
        _context.ShowGrid = GridToggleButton.IsChecked == true;
        GridMenuItem.IsChecked = _context.ShowGrid;
        _context.RequestRedraw();
    }

    private void SnapToggleButton_OnToggled(object sender, RoutedEventArgs e)
    {
        _context.SnapToGrid = SnapToggleButton.IsChecked == true;
        SnapMenuItem.IsChecked = _context.SnapToGrid;
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

    private void DarkModeMenuItem_OnClick(object sender, RoutedEventArgs e) => SwitchTheme(ThemeMode.Dark);

    private void LightModeMenuItem_OnClick(object sender, RoutedEventArgs e) => SwitchTheme(ThemeMode.Light);

    private void SwitchTheme(ThemeMode mode)
    {
        ThemeManager.Apply(mode);
        UpdateThemeMenuMarkers();
    }

    /// <summary>Marks the active theme with the menu check dot.</summary>
    private void UpdateThemeMenuMarkers()
    {
        var dark = ThemeManager.Current == ThemeMode.Dark;
        ViewDarkModeMenuItem.IsChecked = dark;
        ViewLightModeMenuItem.IsChecked = !dark;
    }

    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        new Views.AboutDialog { Owner = this }.ShowDialog();
    }
}
