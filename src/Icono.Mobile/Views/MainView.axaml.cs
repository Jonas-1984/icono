using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Icono.Core;
using Icono.Editor;
using Icono.IO;

namespace Icono.Mobile.Views;

public partial class MainView : UserControl
{
    /// <summary>The scale a new drawing starts with (same as the Windows program, MainWindow.DefaultScale).</summary>
    private const double DefaultScale = 40;

    private const string Hint = "Tap: select · tap a dimension number of a wall or door: type its length · drag an object: move it · " +
                                "drag the paper or two fingers: move and zoom · double tap: fit page";

    private static readonly FilePickerFileType IconoFiles = new("icono drawing")
    {
        Patterns = ["*.icn", "*.icono"],
        // .icn has no MIME type Android knows: every file is offered and the content decides
        MimeTypes = ["*/*"],
    };

    private readonly EditorContext _context = new();

    /// <summary>The file the drawing came from or was last saved to (null = never saved).</summary>
    private IStorageFile? _file;

    private string _name = "Untitled";

    /// <summary><see cref="Icono.Editor.Undo.UndoManager.Version"/> at the last save / open: a different value means unsaved changes.</summary>
    private int _savedVersion;

    private TaskCompletionSource<QuestionAnswer>? _question;

    private enum QuestionAnswer { Cancel, Discard, Save }

    public MainView()
    {
        InitializeComponent();

        Canvas.Context = _context;
        Properties.Context = _context;
        Canvas.ZoomChanged += (_, _) => ZoomText.Text = $"{Canvas.Zoom * 100:0} %";
        _context.UndoManager.PropertyChanged += OnHistoryChanged;
        _context.SelectedShapes.CollectionChanged += (_, _) => UpdateCommands();

        BuildTools();
        SetUpText();
        SetUpAbout();
        LoadDocument(DocumentFile.From(NewDocument()), null, "Untitled");
        StatusText.Text = Hint;
    }

    private bool HasUnsavedChanges => _context.UndoManager.Version != _savedVersion;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.BackRequested += OnBackRequested;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.BackRequested -= OnBackRequested;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>An empty A4 page, as the Windows program starts.</summary>
    private static Document NewDocument()
    {
        var document = new Document
        {
            Width = Document.A4Width,
            Height = Document.A4Height,
            BackgroundMode = BackgroundMode.SolidColor,
            ScaleDenominator = DefaultScale,
        };
        document.Layers.Add(new Layer { Name = "Layer 1" });
        return document;
    }

    /// <summary>Puts a file's drawing into the editor (the Document instance stays, everything is wired to it).</summary>
    private void LoadDocument(DocumentFile file, IStorageFile? source, string name)
    {
        Canvas.CancelPath();
        file.ApplyTo(_context.Document);
        _context.ResetAfterDocumentReplaced();
        _file = source;
        _name = name;
        _savedVersion = _context.UndoManager.Version;
        Canvas.FitToView();
        UpdateTitle();
        UpdateCommands();
    }

    private void OnHistoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Icono.Editor.Undo.UndoManager.Version))
        {
            UpdateTitle();
            UpdateCommands();
        }
    }

    private void UpdateTitle() => TitleText.Text = (HasUnsavedChanges ? "● " : "") + _name;

    private void UpdateCommands()
    {
        UndoButton.IsEnabled = _context.UndoManager.CanUndo;
        RedoButton.IsEnabled = _context.UndoManager.CanRedo;
        var anything = _context.SelectedShapes.Count > 0;
        DuplicateButton.IsEnabled = anything;
        DeleteButton.IsEnabled = anything;

        var selected = _context.SelectedShapes;
        StatusText.Text = _context.ActiveTool != ActiveTool.Select || Canvas.AreaMode ? ToolHint : selected.Count switch
        {
            0 => Hint,
            1 => "Selected: " + Describe(selected[0]) + (selected[0].Locked ? " (locked)" : ""),
            _ => $"{selected.Count} objects selected",
        };
    }

    private static string Describe(Shape shape) =>
        !string.IsNullOrWhiteSpace(shape.Name) ? shape.Name
        : System.Text.RegularExpressions.Regex.Replace(shape.Kind.ToString(), "(?<=[a-z0-9])(?=[A-Z])", " ");

    // ---------------------------------------------------------------- file commands

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmLosingChangesAsync())
        {
            return;
        }

        LoadDocument(DocumentFile.From(NewDocument()), null, "Untitled");
        StatusText.Text = "New page (A4, scale 1:" + DefaultScale + ")";
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmLosingChangesAsync())
        {
            return;
        }

        // icono's own file window (with Cancel); the Android one only for "Other location..."
        if (await AskOpenFileAsync() is not { } picked)
        {
            return;
        }

        try
        {
            StatusText.Text = "Opening " + picked.Name + " ...";
            DocumentFile file;
            await using (var stream = await picked.OpenReadAsync())
            {
                file = await Task.Run(() => DocumentFile.Load(stream));
            }

            LoadDocument(file, picked, picked.Name);
            var document = _context.Document;
            StatusText.Text = $"{picked.Name}: {document.PageCount} page(s), {document.Layers.Sum(l => l.Shapes.Count)} object(s), " +
                              $"scale 1:{document.ScaleDenominator:0.##}";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = picked.Name + " could not be opened: " + ex.Message;
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: true);

    /// <summary>Saves to the current file, or asks for one (Save as, or a drawing never saved). False when nothing was saved.</summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        var target = saveAs ? null : _file;
        if (target is null)
        {
            // icono's own file window (with Cancel); the Android one only for "Other location..."
            var suggested = Path.GetFileNameWithoutExtension(_name);
            target = await AskSaveFileAsync(
                saveAs ? "Save the drawing as" : "Save the drawing",
                string.IsNullOrWhiteSpace(suggested) ? "Untitled" : suggested,
                DocumentFile.Extension,
                new FilePickerFileType("icono drawing") { Patterns = ["*.icn"], MimeTypes = ["application/octet-stream"] });
            if (target is null)
            {
                return false;
            }
        }

        try
        {
            await using (var stream = await target.OpenWriteAsync())
            {
                if (stream.CanSeek)
                {
                    stream.SetLength(0); // a shorter drawing must not leave the end of the old one behind
                }

                DocumentFile.Save(_context.Document, stream);
            }

            _file = target;
            _name = target.Name;
            _savedVersion = _context.UndoManager.Version;
            UpdateTitle();
            StatusText.Text = "Saved: " + target.Name;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = target.Name + " could not be saved: " + ex.Message;
            return false;
        }
    }

    private IStorageProvider? Storage()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            StatusText.Text = "This device cannot open or save files.";
            return null;
        }

        return storage;
    }

    // ---------------------------------------------------------------- edit commands

    private void OnUndo(object? sender, RoutedEventArgs e) => _context.Undo();

    private void OnRedo(object? sender, RoutedEventArgs e) => _context.Redo();

    private void OnDuplicate(object? sender, RoutedEventArgs e) => _context.DuplicateSelection();

    private void OnDelete(object? sender, RoutedEventArgs e) => _context.DeleteSelection();

    private void OnFit(object? sender, RoutedEventArgs e) => Canvas.FitToView();

    private void OnToggleProperties(object? sender, RoutedEventArgs e)
    {
        PropertiesPane.IsVisible = !PropertiesPane.IsVisible;
        PropertiesButton.Classes.Set("on", PropertiesPane.IsVisible);
    }

    // ---------------------------------------------------------------- unsaved changes

    /// <summary>The Android back button closes the app: with unsaved changes it asks first.</summary>
    private async void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (IsTextOpen)
        {
            e.Handled = true;
            CloseText();
            return;
        }

        if (AboutLayer.IsVisible)
        {
            e.Handled = true;
            CloseAbout();
            return;
        }

        if (ExportLayer.IsVisible)
        {
            e.Handled = true;
            ExportLayer.IsVisible = false;
            return;
        }

        if (IsFilesOpen)
        {
            e.Handled = true;
            CloseFiles(null);
            return;
        }

        if (_question is not null)
        {
            e.Handled = true;
            Answer(QuestionAnswer.Cancel);
            return;
        }

        if (!HasUnsavedChanges)
        {
            return;
        }

        e.Handled = true;
        if (await ConfirmLosingChangesAsync())
        {
            _savedVersion = _context.UndoManager.Version;
            UpdateTitle();
            StatusText.Text = "Press Back again to close icono.";
        }
    }

    /// <summary>True when the drawing may be replaced: no unsaved changes, or they were saved, or the user lets them go.</summary>
    private async Task<bool> ConfirmLosingChangesAsync()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        QuestionTitle.Text = "Save the changes?";
        QuestionText.Text = $"\"{_name}\" has changes that are not saved yet.";
        QuestionLayer.IsVisible = true;
        _question = new TaskCompletionSource<QuestionAnswer>();
        var answer = await _question.Task;
        _question = null;
        QuestionLayer.IsVisible = false;

        return answer switch
        {
            QuestionAnswer.Discard => true,
            QuestionAnswer.Save => await SaveAsync(saveAs: false),
            _ => false,
        };
    }

    private void Answer(QuestionAnswer answer) => _question?.TrySetResult(answer);

    private void OnQuestionCancel(object? sender, RoutedEventArgs e) => Answer(QuestionAnswer.Cancel);

    private void OnQuestionDiscard(object? sender, RoutedEventArgs e) => Answer(QuestionAnswer.Discard);

    private void OnQuestionSave(object? sender, RoutedEventArgs e) => Answer(QuestionAnswer.Save);
}
