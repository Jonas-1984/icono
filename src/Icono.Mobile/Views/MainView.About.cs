using System.Globalization;
using Avalonia.Controls;
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
