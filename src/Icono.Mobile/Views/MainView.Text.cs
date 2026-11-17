using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Icono.Core;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>Typing text: a new text object, a text object, or the text inside a shape. What is typed is kept as on Windows
/// (<see cref="TextEditing"/>): an empty text object is removed, an empty shape text taken away.</summary>
public partial class MainView
{
    /// <summary>What OK does with the typed text (null while the text box is closed).</summary>
    private Action<string?>? _textCommit;

    private bool IsTextOpen => _textCommit is not null;

    private void SetUpText()
    {
        Canvas.TextRequested += (_, request) => OpenText(request);
        Properties.EditTextRequested += (_, shape) =>
            OpenText(new TextRequest(shape, _context.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape)), shape.X, shape.Y));
        Properties.RenameLayerRequested += (_, layer) =>
            OpenTextBox("Layer name", "", layer.Name, multiline: false, name => _context.RenameLayer(layer, name));
        Properties.TextBoxRequested += (_, request) => OpenTextBox(request.Title, request.Note, request.Text, multiline: false, request.Commit);
        TextEntry.KeyDown += OnTextKeyDown;

        // tap the number of a wall's dimension (or of a door / window): type the length
        Canvas.LengthRequested += (_, request) => OpenTextBox(request.Title, request.Note, request.Text, multiline: false, request.Commit);

        // what the shared editor code says (a length that cannot be read, a room that closed, a page that was added ...)
        _context.StatusMessage += message => StatusText.Text = message;
    }

    private void OpenText(TextRequest request)
    {
        var isLabel = request.Shape is { Kind: not ShapeKind.Text };
        var title = request.Shape switch
        {
            null => "New text",
            { Kind: ShapeKind.Text } => "Text",
            _ => "Text in the shape",
        };
        var note = isLabel ? "A new line with Enter. Leave it empty to take the text away." : "Leave it empty to remove the text.";

        // a text object is one line (as on Windows), the text in a shape may have several
        OpenTextBox(title, note, request.Shape?.TextContent ?? string.Empty, multiline: isLabel, text =>
        {
            if (request.Shape is { Kind: not ShapeKind.Text } shape)
            {
                TextEditing.CommitLabel(_context, shape, text);
            }
            else
            {
                TextEditing.CommitTextObject(_context, request.Shape, request.Layer, request.X, request.Y, text);
            }
        });
    }

    /// <summary>Asks for a text at the top of the screen (the keyboard comes up); OK hands it to <paramref name="commit"/>.</summary>
    private void OpenTextBox(string title, string note, string text, bool multiline, Action<string?> commit)
    {
        _textCommit = commit;
        TextTitle.Text = title;
        TextNote.Text = note;
        TextNote.IsVisible = note.Length > 0;
        TextEntry.AcceptsReturn = multiline;
        TextEntry.Text = text;
        TextLayer.IsVisible = true;

        // focus after the layer is shown, so Android brings up the keyboard
        Dispatcher.UIThread.Post(() =>
        {
            TextEntry.Focus(NavigationMethod.Tab);
            TextEntry.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void CloseText()
    {
        _textCommit = null;
        TextLayer.IsVisible = false;
        Canvas.Focus();
    }

    private void OnTextOk(object? sender, RoutedEventArgs e)
    {
        if (_textCommit is not { } commit)
        {
            return;
        }

        var text = TextEntry.Text;
        CloseText();
        commit(text);
    }

    private void OnTextCancel(object? sender, RoutedEventArgs e) => CloseText();

    private void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        // a text object ends with Enter (it is one line); the text in a shape keeps Enter for new lines
        if (e.Key == Key.Enter && !TextEntry.AcceptsReturn)
        {
            OnTextOk(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseText();
            e.Handled = true;
        }
    }
}
