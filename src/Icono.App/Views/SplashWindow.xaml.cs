using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Icono.App.State;

namespace Icono.App.Views;

/// <summary>The start screen: the program icon fades in out of a blur, the name ICONO and the sentence "Design Made Simple" follow, then a line
/// fills with its percentage at the right. After <see cref="Duration"/> the window reports <see cref="Finished"/> and the application
/// opens. A click or a key skips it.</summary>
public partial class SplashWindow : Window
{
    /// <summary>How long the start screen is shown.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);

    // The line starts when the sentence has appeared and is full 0.2 s before the end (it follows the storyboard in the XAML).
    private const double LineStart = 2.0;
    private const double LineLength = 3.8;

    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _finished;

    public SplashWindow()
    {
        InitializeComponent();

        // letter-spaced name; the colours follow the theme the user chose (opaque, also for the glass themes)
        NameText.Text = string.Join(' ', "ICONO".AsEnumerable());
        var dark = ThemeManager.Current == ThemeMode.Dark;
        ShadowHost.Background = Brush(dark ? 0x101114 : 0xE9ECF0);
        Card.Background = Glass(dark ? "F01A1C21" : "F4FBFCFD", dark ? "F2121317" : "F4ECEEF2");
        Card.BorderBrush = Glass(dark ? "66FFFFFF" : "AAFFFFFF", dark ? "12FFFFFF" : "33B7BDC8", diagonal: true);
        GlowA.Opacity = 0;
        GlowB.Opacity = 0;
        NameText.Foreground = Brush(dark ? 0xF1F2F4 : 0x1B2332);
        TaglineText.Foreground = Brush(dark ? 0xA7ADB7 : 0x5B6472);
        PercentText.Foreground = Brush(dark ? 0xA7ADB7 : 0x5B6472);
        Track.Background = Brush(dark ? 0x2A2C31 : 0xDDE0E5);

        _timer.Tick += (_, _) => UpdateProgress();
    }

    /// <summary>Raised once, after <see cref="Duration"/> or when the user skips the start screen.</summary>
    public event Action? Finished;

    /// <summary>A two-colour gradient (ARGB hex), top to bottom or corner to corner: the dark glass of the card and the light on its rim.</summary>
    private static LinearGradientBrush Glass(string from, string to, bool diagonal = false)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString("#" + from), (Color)ColorConverter.ConvertFromString("#" + to),
            diagonal ? new Point(0, 0) : new Point(0.5, 0), diagonal ? new Point(1, 1) : new Point(0.5, 1));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Brush(int rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }

    private void Window_OnContentRendered(object? sender, EventArgs e)
    {
        _clock.Restart();
        ((Storyboard)Resources["Intro"]).Begin(this);
        _timer.Start();
    }

    private void UpdateProgress()
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        var progress = Math.Clamp((seconds - LineStart) / LineLength, 0, 1);
        PercentText.Text = $"{(int)Math.Round(progress * 100)}%";

        if (seconds >= Duration.TotalSeconds)
        {
            Finish();
        }
    }

    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _timer.Stop();
        Finished?.Invoke();
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Finish();

    private void Window_OnKeyDown(object sender, KeyEventArgs e) => Finish();
}
