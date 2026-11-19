using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace Icono.Mobile.Views;

/// <summary>About icono (tap the logo): the version, and the test terms, privacy notice and licence notices that go with every copy of the app.
/// The terms and the privacy notice are the texts of the Windows installer, in German on a German tablet, else in English.</summary>
public partial class MainView
{
    /// <summary>The version shown in About (the Android app sets it from its package, e.g. "0.1.0-test.1").</summary>
    public static string AppVersion { get; set; } = "";

    private static bool German => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    private void SetUpAbout()
    {
        LogoPlate.Tapped += (_, _) => OpenAbout();
        AboutTermsButton.Content = German ? "Testbedingungen" : "Test terms";
        AboutPrivacyButton.Content = German ? "Datenschutz" : "Privacy";
        AboutNoticesButton.Content = German ? "Lizenzen" : "Licences";
        BuildLookChooser();
        AppTheme.Changed += BuildLookChooser; // the marks follow a change of the look
    }

    /// <summary>The colour design of the app, as in the Windows program: Mode (Dark / Light) and Style (Soft / Pro / Glass). Kept for the next start (AppTheme).</summary>
    private void BuildLookChooser()
    {
        AboutLookHost.Children.Clear();
        AboutLookHost.Children.Add(LookLabel("MODE"));
        AboutLookHost.Children.Add(LookSegments([("Dark", ThemeMode.Dark), ("Light", ThemeMode.Light)], AppTheme.Mode, mode => AppTheme.Set(mode, AppTheme.Style)));
        AboutLookHost.Children.Add(LookLabel("STYLE"));
        AboutLookHost.Children.Add(LookSegments(
            [("Soft", ThemeStyle.Soft), ("Pro", ThemeStyle.Pro), ("Glass", ThemeStyle.Glass)], AppTheme.Style, style => AppTheme.Set(AppTheme.Mode, style)));
    }

    private static TextBlock LookLabel(string text) => new()
    {
        Text = text, FontSize = 10.5, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = AppTheme.Brush("MutedTextBrush"),
        Margin = new Avalonia.Thickness(2, 2, 0, 0),
    };

    private static Border LookSegments<T>(IReadOnlyList<(string Name, T Value)> options, T current, Action<T> choose)
    {
        var grid = new UniformGrid { Columns = options.Count };
        foreach (var (name, value) in options)
        {
            var key = new Button { Content = name, Classes = { "seg" } };
            key.Classes.Set("on", EqualityComparer<T>.Default.Equals(value, current));
            key.Click += (_, _) => choose(value);
            grid.Children.Add(key);
        }

        return new Border { Classes = { "segPlate" }, Child = grid };
    }

    private void OpenAbout()
    {
        AboutVersion.Text = "Version " + (string.IsNullOrEmpty(AppVersion) ? "-" : AppVersion);
        ShowDocument(German ? "Testbedingungen-DE.txt" : "Terms-EN.txt", AboutTermsButton);
        AboutLayer.IsVisible = true;
    }

    private void CloseAbout() => AboutLayer.IsVisible = false;

    private void OnAboutTerms(object? sender, RoutedEventArgs e) => ShowDocument(German ? "Testbedingungen-DE.txt" : "Terms-EN.txt", AboutTermsButton);

    private void OnAboutPrivacy(object? sender, RoutedEventArgs e) => ShowDocument(German ? "Datenschutz-DE.txt" : "Privacy-EN.txt", AboutPrivacyButton);

    private void OnAboutNotices(object? sender, RoutedEventArgs e)
    {
        var page = new StackPanel { Spacing = 26 };
        page.Children.Add(AboutDocument.Build(Read("Notices/THIRD-PARTY-NOTICES.txt"), numberedHeadings: false));
        page.Children.Add(AboutDocument.Build("Selawik font - SIL Open Font License 1.1\n-------\n" + Read("Notices/Selawik-OFL.txt"), numberedHeadings: false));
        page.Children.Add(AboutDocument.Build(Read("Notices/THIRD-PARTY-NOTICES-DOTNET.txt"), numberedHeadings: false));
        AboutPage.Content = page;
        AboutScroll.Offset = default;
        MarkAboutTab(AboutNoticesButton);
    }

    private void MarkAboutTab(Avalonia.Controls.Button current)
    {
        foreach (var button in new[] { AboutTermsButton, AboutPrivacyButton, AboutNoticesButton })
        {
            button.Classes.Set("on", ReferenceEquals(button, current));
        }
    }

    private void OnAboutClose(object? sender, RoutedEventArgs e) => CloseAbout();

    private async void OnAboutWebsite(object? sender, RoutedEventArgs e) => await OpenLinkAsync("https://www.bitbins.de");

    private async void OnAboutContact(object? sender, RoutedEventArgs e) => await OpenLinkAsync("mailto:info.de?subject=icono%20tablet");

    /// <summary>Hands a link to the browser or the mail app of the tablet (only when the button is tapped).</summary>
    private async Task OpenLinkAsync(string address)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher || !await launcher.LaunchUriAsync(new Uri(address)))
            {
                StatusText.Text = "No app on this tablet could open " + address;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or UriFormatException)
        {
            StatusText.Text = "The link could not be opened: " + ex.Message;
        }
    }

    private void ShowDocument(string name, Avalonia.Controls.Button tab)
    {
        AboutPage.Content = AboutDocument.Build(Read("Docs/" + name), numberedHeadings: true);
        AboutScroll.Offset = default;
        MarkAboutTab(tab);
    }

    private static string Read(string path)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Icono.Mobile/Assets/" + path));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException)
        {
            return "(missing: " + path + ")";
        }
    }
}
