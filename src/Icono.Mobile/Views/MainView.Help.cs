using Avalonia.Interactivity;

namespace Icono.Mobile.Views;

/// <summary>The quick help (the ? key in the top bar): the gestures and tools of the tablet app in a few sections on white paper, in German on a German
/// tablet, else in English. The text is a plain file (Assets/Help) that <see cref="AboutDocument"/> turns into a readable page.</summary>
public partial class MainView
{
    private bool _helpBuilt;

    private void OnHelp(object? sender, RoutedEventArgs e)
    {
        if (!_helpBuilt)
        {
            HelpPage.Content = AboutDocument.Build(ReadHelp(German ? "Hilfe-DE.txt" : "Help-EN.txt"), numberedHeadings: true);
            HelpTitle.Text = German ? "Hilfe" : "Help";
            _helpBuilt = true;
        }

        HelpScroll.Offset = default;
        HelpLayer.IsVisible = true;
    }

    private void CloseHelp() => HelpLayer.IsVisible = false;

    private void OnHelpClose(object? sender, RoutedEventArgs e) => CloseHelp();

    private static string ReadHelp(string name)
    {
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Icono.Mobile/Assets/Help/" + name));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException)
        {
            return "(missing: " + name + ")";
        }
    }
}
