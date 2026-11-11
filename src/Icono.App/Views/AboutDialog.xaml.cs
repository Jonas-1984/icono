using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
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
        VersionText.Text = !string.IsNullOrWhiteSpace(full) ? full : version is null ? "—" : $"{version.Major}.{version.Minor}.{version.Build}";
        PlatformText.Text = $"{RuntimeInformation.FrameworkDescription} · WPF + SkiaSharp";
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

    private void Card_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
