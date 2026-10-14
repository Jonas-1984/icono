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

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "—" : $"{version.Major}.{version.Minor}.{version.Build}";
        PlatformText.Text = $"{RuntimeInformation.FrameworkDescription} · WPF + SkiaSharp";
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
