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

        var selected = HandwritingToText.IsSelection(_context);
        var groups = selected ? HandwritingToText.Groups(strokes).Count : HandwritingToText.RecentGroups(_context).Count;
        var count = strokes.Count == 1 ? "1 stroke" : $"{strokes.Count} strokes";
        InkText.Text = _recognizing ? "Reading ..."
            : selected ? (groups > 1 ? $"{count} in {groups} groups" : count)
            : groups > 1 ? $"Writing {groups - Math.Clamp(_context.InkGroupIndex, 0, groups - 1)} of {groups}: {count}"
            : count;
        InkOtherButton.IsVisible = !selected && groups > 1 && !_recognizing;
        InkConvertButton.Content = selected && groups > 1 ? $"Convert {groups} groups" : "Convert to text";
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

    /// <summary>"Other writing": the next older group of strokes that lie together is the one that is offered (and outlined).</summary>
    private void OnInkOther(object? sender, RoutedEventArgs e)
    {
        var groups = HandwritingToText.RecentGroups(_context).Count;
        if (groups > 1)
        {
            _context.SetInkGroup((_context.InkGroupIndex + 1) % groups);
        }
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

        // the strokes the user selected can be several pieces of writing far apart: every group gets its own text; the recent writing is one group
        var groups = HandwritingToText.IsSelection(_context) ? HandwritingToText.Groups(strokes) : [strokes];
        _recognizing = true;
        UpdateInkBar();
        var made = new List<string>();
        string? problem = null;
        foreach (var group in groups)
        {
            HandwritingResult result;
            try
            {
                result = await recognize(HandwritingToText.Sample(group), Handwriting.Language, message => StatusText.Text = message);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
            {
                result = new HandwritingResult(null, "Handwriting could not be recognized: " + ex.Message);
            }

            if (result.Text is { } text && HandwritingToText.Replace(_context, group, text))
            {
                made.Add(text.Trim());
            }
            else
            {
                problem ??= result.Problem;
            }
        }

        _recognizing = false;
        if (made.Count == 1 && groups.Count == 1)
        {
            StatusText.Text = $"Text: \"{made[0]}\" - tap it twice to change it, the Properties show its font and size.";
        }
        else if (made.Count > 0)
        {
            StatusText.Text = $"{made.Count} text{(made.Count == 1 ? "" : "s")} made: {string.Join(", ", made.Select(t => $"\"{t}\""))}" +
                              (made.Count < groups.Count ? $" ({groups.Count - made.Count} could not be read)" : "") + ".";
        }
        else
        {
            StatusText.Text = problem ?? "Nothing was changed (the strokes are gone or locked).";
        }

        UpdateInkBar();
    }
}
