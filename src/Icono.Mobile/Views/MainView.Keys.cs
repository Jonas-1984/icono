using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>The keyboard shortcuts of the Windows program, for a tablet with a keyboard (a cover or a Bluetooth keyboard): single keys choose the tools,
/// Ctrl + key edits, files and zoom, as in the Windows "Keyboard Shortcuts" window. Nothing happens while a text is being typed.</summary>
public partial class MainView
{
    /// <summary>The tool keys of the Windows program: the key and the name of the tool in the tool bar.</summary>
    private static readonly (Key Key, string Tool)[] ToolKeys =
    [
        (Key.V, "Select"), (Key.I, "Colour picker"), (Key.G, "Region colour"), (Key.W, "Wall"), (Key.D, "Dimension"), (Key.M, "Ruler"),
        (Key.B, "Distance"), (Key.X, "Cut wall"), (Key.Q, "Match"), (Key.P, "Pen"), (Key.T, "Text"),
    ];

    private void SetUpKeys() => AddHandler(KeyDownEvent, OnShortcutKey, RoutingStrategies.Tunnel);

    private async void OnShortcutKey(object? sender, KeyEventArgs e)
    {
        // a text box has the keys while it is being typed in; the dialogs on top of the drawing too
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox || IsAnyDialogOpen)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (alt)
        {
            return;
        }

        e.Handled = true;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z when shift: _context.Redo(); break;
                case Key.Z: _context.Undo(); break;
                case Key.Y: _context.Redo(); break;
                case Key.C: _context.CopySelection(); break;
                case Key.X when shift: _context.ExplodeSelection(); break;
                case Key.X: _context.CutSelection(); break;
                case Key.V: _context.PasteClipboard(); break;
                case Key.A: _context.SelectAll(); break;
                case Key.D: _context.DuplicateSelection(); break;
                case Key.G when shift: _context.UngroupSelection(); break;
                case Key.G: _context.GroupSelection(); break;
                case Key.N: OnNew(this, e); break;
                case Key.O: OnOpen(this, e); break;
                case Key.S when shift: await SaveAsync(saveAs: true); break;
                case Key.S: await SaveAsync(saveAs: false); break;
                case Key.E: OnExport(this, e); break;
                case Key.P: OnExportPrint(this, e); break;
                case Key.OemPlus or Key.Add: Canvas.ZoomBy(1.25); break;
                case Key.OemMinus or Key.Subtract: Canvas.ZoomBy(0.8); break;
                case Key.D0 or Key.NumPad0: Canvas.ZoomTo(1); break;
                case Key.D1 or Key.NumPad1: Canvas.FitToView(); break;
                default: e.Handled = false; break;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                _context.DeleteSelection();
                break;
            case Key.Up or Key.Down or Key.Left or Key.Right when _context.SelectedShapes.Count > 0 && !Canvas.IsDrawingPath:
            {
                // the selection moves by 1 px (10 px with Shift), as in the Windows program
                var step = shift ? 10.0 : 1.0;
                _context.NudgeSelection(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
                break;
            }
            case Key.Enter when Canvas.IsDrawingPath:
                Canvas.FinishPath(); // as the Done key
                break;
            case Key.Enter when _context.ActiveTool != ActiveTool.Select:
                ChooseTool(Tools[0]);
                break;
            case Key.F3:
                OnOSnapPill(this, e);
                break;
            case Key.F1:
                OnHelp(this, e);
                break;
            case Key.Escape:
                if (Canvas.IsDrawingPath)
                {
                    Canvas.CancelPath();
                }
                else if (_context.ActiveTool != ActiveTool.Select || Canvas.AreaMode)
                {
                    ChooseTool(Tools[0]);
                }
                else
                {
                    _context.ClearSelection();
                }

                break;
            default:
                var tool = shift ? null : ToolKeys.FirstOrDefault(k => k.Key == e.Key).Tool;
                if (tool is null)
                {
                    e.Handled = false;
                    break;
                }

                ChooseTool(Tools.First(t => t.Name == tool));

                break;
        }
    }

    /// <summary>True while a dialog lies over the drawing (the keys then belong to it).</summary>
    private bool IsAnyDialogOpen =>
        FilesLayer.IsVisible || ExportLayer.IsVisible || NewLayer.IsVisible || LengthLayer.IsVisible || AboutLayer.IsVisible || HelpLayer.IsVisible || TextLayer.IsVisible || QuestionLayer.IsVisible;
}
