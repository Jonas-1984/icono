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

    private void LicensesLink_OnClick(object sender, RoutedEventArgs e)
    {
        var file = System.IO.Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.FileNotFoundException)
        {
            ThemedMessageBox.Show(this, "The file THIRD-PARTY-NOTICES.txt next to icono.exe could not be opened.", "Licenses", MessageBoxButton.OK, MessageBoxImage.Information);
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
