using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Icono.App.State;
using Icono.App.Views;
using Icono.Core;
using Icono.IO;
using Icono.Rendering;

namespace Icono.App;

/// <summary>
/// File handling (new / open / save / export / recent files), the editing, view, tool and design commands behind the menu
/// bar, keyboard shortcuts and unsaved-changes tracking.
/// </summary>
public partial class MainWindow
{
    private const string RepositoryUrl = "https://github.com/Jonas-1984/icono";

    private string? _filePath;

    /// <summary>JSON of the document as it was last saved / created / opened — compared with the current JSON to detect unsaved changes.</summary>
    private string _savedFingerprint = string.Empty;

    /// <summary>Undo version at the last save / new / open — a cheap way to drive the "*" in the title without serializing the whole document.</summary>
    private int _savedVersion;

    private string DocumentName => _filePath is null ? "Untitled" : Path.GetFileNameWithoutExtension(_filePath);

    private bool HasUnsavedChanges() => DocumentFile.Serialize(_context.Document) != _savedFingerprint;

    /// <summary>Called once from the constructor: remembers the starting document as "saved" and keeps the title up to date.</summary>
    private void InitializeDocumentTracking()
    {
        _savedFingerprint = DocumentFile.Serialize(_context.Document);
        _savedVersion = _context.UndoManager.Version;
        _context.UndoManager.PropertyChanged += (_, _) => UpdateTitle();
        _context.PropertyChanged += Context_OnPropertyChangedForWindow;
        UpdateTitle();
        UpdateViewMenuMarkers();
    }

    private void Context_OnPropertyChangedForWindow(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorContext.ActiveTool) or nameof(EditorContext.SelectedShapeKind) or nameof(EditorContext.CurrentDimensionKind))
        {
            SyncToolUi();
        }
    }

    private void UpdateTitle()
    {
        // Cheap check for the title; the exact (serialized) comparison is only done when it matters (new / open / close).
        var title = $"{DocumentName}{(_context.UndoManager.Version != _savedVersion ? " *" : string.Empty)}";
        Title = $"{title} - icono";
        TitleBar.SetDocumentTitle(title + "  -  icono");
    }

    private void ShowStatus(string message) => StatusBar.ShowMessage(message);

    // ----- file: new / open / save / export ------------------------------------------------------

    /// <summary>Asks what to do with unsaved changes. True = go on (saved or discarded), false = the user cancelled.</summary>
    private bool ConfirmDiscardChanges()
    {
        if (!HasUnsavedChanges())
        {
            return true;
        }

        var choice = ThemedMessageBox.Choose(
            this, "Unsaved changes", $"Save the changes to \"{DocumentName}\" before continuing?",
            ["Save", "Don't save", "Cancel"], defaultIndex: 0, cancelIndex: 2, MessageBoxImage.Question);

        return choice switch
        {
            0 => SaveDocument(),
            1 => true,
            _ => false,
        };
    }

    /// <summary>Replaces the contents of the open document and resets everything that belongs to the old one (history, selection, view).</summary>
    private void ReplaceDocument(DocumentFile file, string? path)
    {
        file.ApplyTo(_context.Document);
        _filePath = path;
        _context.ResetAfterDocumentReplaced();
        PropertiesPanel.RefreshDocumentSettings();
        CanvasHost.ResetView();
        FitPageToWindow();
        _savedFingerprint = DocumentFile.Serialize(_context.Document);
        _savedVersion = _context.UndoManager.Version;
        UpdateTitle();
    }

    private void NewMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var dialog = new NewDocumentDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ReplaceDocument(
            new DocumentFile
            {
                Width = dialog.PageWidth,
                Height = dialog.PageHeight,
                BackgroundMode = dialog.TransparentBackground ? BackgroundMode.Transparent : BackgroundMode.SolidColor,
                Layers = [new LayerFile { Name = "Layer 1" }],
            },
            path: null);
        ShowStatus("New document created");
    }

    private void OpenMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open document",
            Filter = $"icono documents (*{DocumentFile.Extension})|*{DocumentFile.Extension}|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenDocument(dialog.FileName);
        }
    }

    private void OpenDocument(string path)
    {
        try
        {
            ReplaceDocument(DocumentFile.Load(path), path);
            RecentFiles.Add(path);
            ShowStatus($"Opened {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            RecentFiles.Remove(path);
            ThemedMessageBox.Show(this, $"\"{Path.GetFileName(path)}\" could not be opened.\n\n{ex.Message}", "Open document",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RecentMenuItem_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, RecentMenuItem))
        {
            return;
        }

        RecentMenuItem.Items.Clear();
        var recent = RecentFiles.Load();
        if (recent.Count == 0)
        {
            RecentMenuItem.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
            return;
        }

        foreach (var path in recent)
        {
            // "_" would be taken as an access-key marker, so it is doubled to show up literally.
            var item = new MenuItem { Header = Path.GetFileName(path).Replace("_", "__"), ToolTip = path, Tag = path };
            item.Click += (_, _) =>
            {
                if (ConfirmDiscardChanges())
                {
                    OpenDocument(path);
                }
            };
            RecentMenuItem.Items.Add(item);
        }

        RecentMenuItem.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear List" };
        clear.Click += (_, _) =>
        {
            foreach (var path in recent)
            {
                RecentFiles.Remove(path);
            }
        };
        RecentMenuItem.Items.Add(clear);
    }

    private void SaveMenuItem_OnClick(object sender, RoutedEventArgs e) => SaveDocument();

    private void SaveAsMenuItem_OnClick(object sender, RoutedEventArgs e) => SaveDocumentAs();

    /// <summary>Saves to the current file (asking for a name first when the document is new). False when cancelled or failed.</summary>
    private bool SaveDocument() => _filePath is null ? SaveDocumentAs() : SaveTo(_filePath);

    private bool SaveDocumentAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save document",
            Filter = $"icono documents (*{DocumentFile.Extension})|*{DocumentFile.Extension}",
            FileName = DocumentName,
            DefaultExt = DocumentFile.Extension,
            AddExtension = true,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog(this) == true && SaveTo(dialog.FileName);
    }

    private bool SaveTo(string path)
    {
        try
        {
            DocumentFile.Save(_context.Document, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, $"The document could not be saved.\n\n{ex.Message}", "Save document",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        _filePath = path;
        _savedFingerprint = DocumentFile.Serialize(_context.Document);
        _savedVersion = _context.UndoManager.Version;
        RecentFiles.Add(path);
        UpdateTitle();
        ShowStatus($"Saved {Path.GetFileName(path)}");
        return true;
    }

    private void ExportMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var document = _context.Document;
        var dialog = new ExportDialog(document, backgroundDefault: document.BackgroundMode != BackgroundMode.Transparent) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            switch (dialog.Format)
            {
                case ExportFormat.Ico:
                    if (AskExportPath("Windows icon (*.ico)|*.ico", ".ico") is { } icoPath)
                    {
                        File.WriteAllBytes(icoPath, DocumentExporter.RenderIco(document, DocumentExporter.IconSizes, dialog.IncludeBackground));
                        ShowStatus($"Exported {Path.GetFileName(icoPath)}");
                    }

                    break;

                case ExportFormat.PngSet:
                    if (AskExportPath("PNG image (*.png)|*.png", ".png") is { } setPath)
                    {
                        var folder = Path.GetDirectoryName(setPath)!;
                        var baseName = Path.GetFileNameWithoutExtension(setPath);
                        foreach (var size in new[] { 16, 32, 64, 128, 256, 512 })
                        {
                            File.WriteAllBytes(
                                Path.Combine(folder, $"{baseName}_{size}.png"),
                                DocumentExporter.RenderPng(document, size, dialog.IncludeBackground));
                        }

                        ShowStatus($"Exported 6 PNG sizes to {folder}");
                    }

                    break;

                default:
                    if (AskExportPath("PNG image (*.png)|*.png", ".png") is { } pngPath)
                    {
                        File.WriteAllBytes(pngPath, DocumentExporter.RenderPng(document, dialog.LongSide, dialog.IncludeBackground));
                        ShowStatus($"Exported {Path.GetFileName(pngPath)}");
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, $"The export failed.\n\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportPngQuickMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var document = _context.Document;
        if (AskExportPath("PNG image (*.png)|*.png", ".png") is not { } path)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(path, DocumentExporter.RenderPng(document, Math.Max(document.Width, document.Height), document.BackgroundMode != BackgroundMode.Transparent));
            ShowStatus($"Exported {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThemedMessageBox.Show(this, $"The export failed.\n\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string? AskExportPath(string filter, string extension)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export",
            Filter = filter,
            FileName = DocumentName,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    // ----- edit -----------------------------------------------------------------------------------

    private void DuplicateMenuItem_OnClick(object sender, RoutedEventArgs e) => _context.DuplicateSelection();

    private void DeleteMenuItem_OnClick(object sender, RoutedEventArgs e) => _context.DeleteSelection();

    private void SelectAllMenuItem_OnClick(object sender, RoutedEventArgs e) => _context.SelectAll();

    private void DeselectMenuItem_OnClick(object sender, RoutedEventArgs e) => _context.ClearSelection();

    // ----- view -----------------------------------------------------------------------------------

    private void ZoomInMenuItem_OnClick(object sender, RoutedEventArgs e) => StatusBar.ZoomBy(1.25);

    private void ZoomOutMenuItem_OnClick(object sender, RoutedEventArgs e) => StatusBar.ZoomBy(0.8);

    private void ActualSizeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        StatusBar.SetZoom(1);
        CanvasHost.ResetView();
    }

    private void FitPageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        FitPageToWindow();
        CanvasHost.ResetView();
    }

    private void GridMenuItem_OnClick(object sender, RoutedEventArgs e) => GridToggleButton.IsChecked = GridMenuItem.IsChecked;

    private void SnapMenuItem_OnClick(object sender, RoutedEventArgs e) => SnapToggleButton.IsChecked = SnapMenuItem.IsChecked;

    private void UpdateViewMenuMarkers()
    {
        GridMenuItem.IsChecked = _context.ShowGrid;
        SnapMenuItem.IsChecked = _context.SnapToGrid;
    }

    // ----- tools / insert / draw ------------------------------------------------------------------

    // ----- Drawing scale (1:N) -----------------------------------------------------------------------

    /// <summary>Fills Design > Scale with the preset scales (1:1, 1:20, 1:40, 1:50, 1:60 ...).</summary>
    private void BuildScaleMenu()
    {
        ScaleMenuItem.Items.Clear();
        foreach (var denominator in Document.ScalePresets)
        {
            var item = new MenuItem
            {
                Header = DrawingScale.PresetLabel(denominator),
                Tag = denominator,
                IsCheckable = false,
            };
            item.Click += ScaleMenuItem_OnClick;
            ScaleMenuItem.Items.Add(item);
        }

        ScaleMenuItem.Items.Add(new Separator());
        var custom = new MenuItem { Header = "Other scale..." };
        custom.Click += (_, _) => PropertiesPanel.FocusCard("CANVAS SIZE");
        ScaleMenuItem.Items.Add(custom);
        UpdateScaleMenuMarkers();
    }

    private void ScaleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: double denominator })
        {
            _context.SetScale(denominator);
        }
    }

    /// <summary>Puts the check dot on the active scale.</summary>
    private void UpdateScaleMenuMarkers()
    {
        foreach (var item in ScaleMenuItem.Items.OfType<MenuItem>())
        {
            item.IsChecked = item.Tag is double d && Math.Abs(d - _context.Document.ScaleDenominator) < 1e-9;
        }
    }

    private void AddDimensionsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<DimensionSet>(tag, out var set))
        {
            _context.AddDimensionsToSelection(set);
        }
    }

    private void ToolMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            ActivateTool(tag);
        }
    }

    /// <summary>Switches to a tool by name: a tool of its own (Select, PanZoom, ...), a shape kind placed by dragging, or a
    /// click-by-click path tool (Polyline, Curve).</summary>
    private void ActivateTool(string tag)
    {
        switch (tag)
        {
            case "Select":
                _context.ActiveTool = ActiveTool.Select;
                break;
            case "PanZoom":
                _context.ActiveTool = ActiveTool.PanZoom;
                break;
            case "AddPoint":
                _context.ActiveTool = ActiveTool.AddPoint;
                break;
            case "DeletePoint":
                _context.ActiveTool = ActiveTool.DeletePoint;
                break;
            case "RegionColor":
                _context.ActiveTool = ActiveTool.RegionColor;
                break;
            case "Eyedropper":
                _context.ActiveTool = ActiveTool.Eyedropper;
                break;
            case "WallCut":
                _context.ActiveTool = ActiveTool.WallCut;
                break;
            case "Measure":
                _context.ActiveTool = ActiveTool.Measure;
                break;
            case "MeasureGap":
                _context.ActiveTool = ActiveTool.MeasureGap;
                break;
            case "Match":
                _context.BeginMatch();
                break;
            case "DimHorizontal":
            case "DimVertical":
            case "DimAligned":
            case "DimAngular":
            case "DimRadius":
            case "DimDiameter":
                _context.CurrentDimensionKind = Enum.Parse<DimensionKind>(tag[3..]);
                _context.SelectedShapeKind = ShapeKind.Dimension;
                _context.ActiveTool = ActiveTool.PathDraw;
                break;
            case "Wall":
            case "Polyline":
            case "Curve":
                _context.SelectedShapeKind = Enum.Parse<ShapeKind>(tag);
                _context.ActiveTool = ActiveTool.PathDraw;
                break;
            default:
                if (Enum.TryParse<ShapeKind>(tag, out var kind))
                {
                    _context.SelectedShapeKind = kind;
                    _context.ActiveTool = ActiveTool.Shape;
                }

                break;
        }
    }

    private void SheetMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        // the menu item flips its own check mark first; the document state decides
        if (_context.Document.Sheet.Enabled != SheetMenuItem.IsChecked)
        {
            _context.ToggleSheet();
        }
    }

    private void InsertImageMenuItem_OnClick(object sender, RoutedEventArgs e) => ToolPanel.ImportLibrary.ImportImageFromDialog();

    private void InsertSvgMenuItem_OnClick(object sender, RoutedEventArgs e) => ToolPanel.ImportLibrary.ImportSvgFromDialog();

    private void NewLayerMenuItem_OnClick(object sender, RoutedEventArgs e) => PropertiesPanel.AddLayer();

    // ----- design ---------------------------------------------------------------------------------

    private void DesignStyleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<DesignStyleKind>(tag, out var kind))
        {
            return;
        }

        if (!PropertiesPanel.ApplyDesignStyle(kind))
        {
            ThemedMessageBox.Show(this, "Select a closed shape (or text) first - design styles do not apply to lines, images or grids.",
                "Design Style", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void FocusCardMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string card })
        {
            return;
        }

        if (!PropertiesPanel.FocusCard(card))
        {
            var hint = card == "LINE" ? "Select a line first." : "Select a shape first to use these options.";
            ThemedMessageBox.Show(this, hint, "Design", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ----- help -----------------------------------------------------------------------------------

    private void ShortcutsMenuItem_OnClick(object sender, RoutedEventArgs e) => new ShortcutsDialog { Owner = this }.ShowDialog();

    private void DocumentationMenuItem_OnClick(object sender, RoutedEventArgs e) => OpenUrl($"{RepositoryUrl}#readme");

    private void UpdatesMenuItem_OnClick(object sender, RoutedEventArgs e) => OpenUrl($"{RepositoryUrl}/releases");

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ThemedMessageBox.Show(this, $"The page could not be opened:\n{url}", "icono", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ----- keyboard shortcuts ---------------------------------------------------------------------

    /// <summary>Ctrl shortcuts that never edit text (file commands): work even while a text box has the focus.</summary>
    private bool HandleFileShortcuts(KeyEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return false;
        }

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.N:
                NewMenuItem_OnClick(this, new RoutedEventArgs());
                return true;
            case Key.O:
                OpenMenuItem_OnClick(this, new RoutedEventArgs());
                return true;
            case Key.S:
                if (shift)
                {
                    SaveDocumentAs();
                }
                else
                {
                    SaveDocument();
                }

                return true;
            case Key.P:
                PrintDocument();
                return true;
            case Key.E:
                ExportMenuItem_OnClick(this, new RoutedEventArgs());
                return true;
            default:
                return false;
        }
    }

    /// <summary>Single-key tool shortcuts and Ctrl editing / zoom shortcuts (only while no text field is being edited).</summary>
    private bool HandleEditorShortcuts(KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (!ctrl && !alt)
        {
            if (_context.ActiveTool == ActiveTool.PathDraw && e.Key is Key.Escape or Key.Enter)
            {
                return false; // handled by the path-drawing logic
            }

            string? tool = e.Key switch
            {
                Key.V => "Select",
                Key.H => "PanZoom",
                Key.I => "Eyedropper",
                Key.G => "RegionColor",
                Key.W => "Wall",
                Key.D => "DimAligned",
                Key.M => "Measure",
                Key.B => "MeasureGap",
                Key.X => "WallCut",
                Key.Q => "Match",
                Key.T => "Text",
                _ => null,
            };

            if (tool is not null && !shift)
            {
                if (tool == "Text")
                {
                    TextButton_OnClick(this, new RoutedEventArgs());
                }
                else
                {
                    ActivateTool(tool);
                }

                return true;
            }

            if (e.Key == Key.Escape)
            {
                // first Esc drops a measurement in progress, the next one leaves the tool
                if (CanvasHost.CancelMeasure())
                {
                    return true;
                }

                if (_context.ActiveTool is ActiveTool.Eyedropper or ActiveTool.RegionColor or ActiveTool.PanZoom or ActiveTool.Shape
                    or ActiveTool.Measure or ActiveTool.MeasureGap or ActiveTool.Match)
                {
                    _context.ActiveTool = ActiveTool.Select;
                }
                else
                {
                    _context.ClearSelection();
                }

                return true;
            }

            return false;
        }

        if (!ctrl)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.A:
                _context.SelectAll();
                return true;
            case Key.D:
                _context.DuplicateSelection();
                return true;
            case Key.G:
                if (shift)
                {
                    _context.UngroupSelection();
                }
                else
                {
                    _context.GroupSelection();
                }

                return true;
            case Key.OemPlus or Key.Add:
                StatusBar.ZoomBy(1.25);
                return true;
            case Key.OemMinus or Key.Subtract:
                StatusBar.ZoomBy(0.8);
                return true;
            case Key.D0 or Key.NumPad0:
                ActualSizeMenuItem_OnClick(this, new RoutedEventArgs());
                return true;
            case Key.D1 or Key.NumPad1:
                FitPageMenuItem_OnClick(this, new RoutedEventArgs());
                return true;
            default:
                return false;
        }
    }
}
