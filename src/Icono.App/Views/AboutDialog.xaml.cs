using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace Icono.App.Views;

/// <summary>Themed "About icono" window (replaces the stock OS MessageBox so it matches the dark
/// neumorphic look of the rest of the app). Borderless; drag the card to move it, Esc/Enter/OK close it.</summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        LogoImage.Source = AppLogo.Load(560);

        // the full version with its test number (0.1.0-test.2), so a tester can say exactly which build they have
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var full = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var versionText = !string.IsNullOrWhiteSpace(full) ? full : version is null ? "—" : $"{version.Major}.{version.Minor}.{version.Build}";
        var platformText = $"{RuntimeInformation.FrameworkDescription} · WPF + SkiaSharp";
        var document = AboutTextBox.Document;
        AddParagraph(document, $"Version: {versionText}\nPlatform: {platformText}", 0);
        AddParagraph(document, "Design & Development: Jonas Shojaei\nBuilt with: Claude (Anthropic)", 10);
        AddLinkParagraph(document, "Website: www.bitbins.de", new Uri("https://www.bitbins.de"), null, 2);
        AddLinkParagraph(document, "E-mail: info@bitbins.de", new Uri("mailto:info@bitbins.de"), null, 2);
        AddLinkParagraph(document, "Licenses: Third-party notices", null, LicensesLink_OnClick, 2);
        AddParagraph(document, "GOOD TO KNOW", 12);
        AddParagraph(document, "Test version. icono is not finished and can contain errors. Back up your drawings regularly.", 3);
        AddParagraph(document, "Privacy. icono sends no data and needs no internet. Settings, recent files and the error log stay on this PC in %LocalAppData%\\Icono.", 3);
        AddParagraph(document, "Your drawings. They belong to you and are saved only where you choose. Do not put personal data of others into drawings you pass on.", 3);
        AddParagraph(document, "Dimensions and plans are aids, not a building document: check them before you build, buy or install anything.", 3);
        AddParagraph(document, "Files from outside (SVG, pictures, documents) are read with safety limits. Open only files you trust.", 3);
        AddLinkParagraph(document, "User manual", null, ManualLink_OnClick, 8);
        AddLinkParagraph(document, "Test terms", null, TermsLink_OnClick, 1);
        AddLinkParagraph(document, "Privacy notice", null, PrivacyLink_OnClick, 1);
    }

    private void AddParagraph(FlowDocument document, string text, double marginTop)
    {
        document.Blocks.Add(new Paragraph(new Run(text))
        {
            Margin = new Thickness(0, marginTop, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("ForegroundTextBrush")
        });
    }

    private void AddLinkParagraph(FlowDocument document, string text, Uri? uri, RoutedEventHandler? click, double marginTop)
    {
        var link = new Hyperlink(new Run(text)) { Foreground = (System.Windows.Media.Brush)FindResource("AccentBlueMidBrush") };
        if (uri is not null)
        {
            link.NavigateUri = uri;
            link.RequestNavigate += Hyperlink_OnRequestNavigate;
        }
        if (click is not null)
            link.Click += click;
        document.Blocks.Add(new Paragraph(link) { Margin = new Thickness(0, marginTop, 0, 0) });
    }

    private void Hyperlink_OnRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Clipboard.SetText(e.Uri.AbsoluteUri.Replace("mailto:", string.Empty));
        }

        e.Handled = true;
    }

    private void LicensesLink_OnClick(object sender, RoutedEventArgs e) => OpenDocument(LocalDocuments.Kind.ThirdParty);

    private void ManualLink_OnClick(object sender, RoutedEventArgs e) => OpenDocument(LocalDocuments.Kind.Manual);

    private void TermsLink_OnClick(object sender, RoutedEventArgs e) => OpenDocument(LocalDocuments.Kind.Terms);

    private void PrivacyLink_OnClick(object sender, RoutedEventArgs e) => OpenDocument(LocalDocuments.Kind.Privacy);

    /// <summary>Opens a document that lies next to icono.exe; says so when it is not there (a build that was not installed).</summary>
    private void OpenDocument(LocalDocuments.Kind kind)
    {
        if (!LocalDocuments.TryOpen(kind))
        {
            ThemedMessageBox.Show(this, $"The file {LocalDocuments.FileName(kind)} next to icono.exe could not be opened.", "icono", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
