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
    private TextRequest? _textRequest;

    private void SetUpText()
    {
        Canvas.TextRequested += (_, request) => OpenText(request);
        Properties.EditTextRequested += (_, shape) =>
            OpenText(new TextRequest(shape, _context.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape)), shape.X, shape.Y));
        TextEntry.KeyDown += OnTextKeyDown;
    }

    private void OpenText(TextRequest request)
    {
        _textRequest = request;
        var isLabel = request.Shape is { Kind: not ShapeKind.Text };

        TextTitle.Text = request.Shape switch
        {
            null => "New text",
            { Kind: ShapeKind.Text } => "Text",
            _ => "Text in the shape",
        };
        TextNote.Text = isLabel
            ? "A new line with Enter. Leave it empty to take the text away."
            : "Leave it empty to remove the text.";

        // a text object is one line (as on Windows), the text in a shape may have several
        TextEntry.AcceptsReturn = isLabel;
        TextEntry.Text = request.Shape?.TextContent ?? string.Empty;
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
        _textRequest = null;
        TextLayer.IsVisible = false;
        Canvas.Focus();
    }

    private void OnTextOk(object? sender, RoutedEventArgs e)
    {
        if (_textRequest is not { } request)
        {
            return;
        }

        var text = TextEntry.Text;
        CloseText();

        if (request.Shape is { Kind: not ShapeKind.Text } shape)
        {
            TextEditing.CommitLabel(_context, shape, text);
        }
        else
        {
            TextEditing.CommitTextObject(_context, request.Shape, request.Layer, request.X, request.Y, text);
        }
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
