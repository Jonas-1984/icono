using Avalonia.Interactivity;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>Handwriting to text: the bar at the top of the drawing that offers "Convert to text" for the strokes just written with the pen (or the
/// handwriting that is selected). The recognizer is the platform's (<see cref="Handwriting.Recognize"/>); the editing is <see cref="HandwritingToText"/>.</summary>
public partial class MainView
{
    private bool _recognizing;

    private void UpdateInkBar()
    {
        if (_context.ActiveTool != ActiveTool.Freehand && _context.RecentInk.Count > 0)
        {
            _context.ForgetInk(); // writing that was left behind is not offered later
        }

        var strokes = HandwritingToText.Strokes(_context);
        var offer = Handwriting.Recognize is not null && strokes.Count > 0
                    && (_context.ActiveTool == ActiveTool.Freehand || _context.SelectedShapes.Any(s => s.Freehand));
        InkBar.IsVisible = offer || _recognizing;
        if (!InkBar.IsVisible)
        {
            return;
        }

        InkText.Text = _recognizing ? "Reading ..." : strokes.Count == 1 ? "1 stroke" : $"{strokes.Count} strokes";
        InkConvertButton.IsEnabled = !_recognizing;
        InkLanguageButton.IsEnabled = !_recognizing;
        InkLanguageButton.Content = Handwriting.Languages.First(l => l.Tag == Handwriting.Language).Name;
    }

    private void OnInkLanguage(object? sender, RoutedEventArgs e)
    {
        var languages = Handwriting.Languages;
        var index = Array.FindIndex(languages, l => l.Tag == Handwriting.Language);
        Handwriting.Language = languages[(index + 1) % languages.Length].Tag;
        UpdateInkBar();
    }

    private async void OnInkConvert(object? sender, RoutedEventArgs e)
    {
        if (_recognizing || Handwriting.Recognize is not { } recognize)
        {
            return;
        }

        var strokes = HandwritingToText.Strokes(_context);
        if (strokes.Count == 0)
        {
            return;
        }

        _recognizing = true;
        UpdateInkBar();
        HandwritingResult result;
        try
        {
            result = await recognize(HandwritingToText.Sample(strokes), Handwriting.Language, message => StatusText.Text = message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
        {
            result = new HandwritingResult(null, "Handwriting could not be recognized: " + ex.Message);
        }

        _recognizing = false;
        if (result.Text is { } text && HandwritingToText.Replace(_context, strokes, text))
        {
            StatusText.Text = $"Text: \"{text}\" - tap it twice to change it, the Properties show its font and size.";
        }
        else
        {
            StatusText.Text = result.Problem ?? "Nothing was changed (the strokes are gone or locked).";
        }

        UpdateInkBar();
    }
}
