using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Icono.Mobile.Views;

/// <summary>A small number pad of frosted glass (a see-through, fading surface) for the number boxes of the Properties, instead of the keyboard of the
/// tablet: the digits 1-9 and 0, comma and point, minus, delete and OK. The first key replaces the number that was there, a later key adds to it.</summary>
public sealed class NumberPad : UserControl
{
    private readonly TextBlock _display = new()
    {
        FontSize = 28, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private string _text;
    private bool _fresh = true;

    /// <param name="title">What the number is (for example "Width").</param>
    /// <param name="unit">The unit shown next to the number (may be empty).</param>
    /// <param name="current">The number as it is now; the first key replaces it.</param>
    public NumberPad(string title, string unit, string current)
    {
        _text = current;

        var heading = new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var close = Key("✕", () => Cancelled?.Invoke(), 14, 0x22);
        close.Width = 34;
        close.Height = 34;
        close.MinHeight = 0;
        close.Padding = new Thickness(0);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(heading);
        Grid.SetColumn(close, 1);
        top.Children.Add(close);

        var unitText = new TextBlock
        {
            Text = unit, FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var screen = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8), MinHeight = 52,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { _display },
            },
        };
        Grid.SetColumn(unitText, 1);
        ((Grid)screen.Child).Children.Add(unitText);

        var keys = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0),
        };
        void Put(Button key, int row, int column)
        {
            Grid.SetRow(key, row);
            Grid.SetColumn(key, column);
            keys.Children.Add(key);
        }

        string[] layout = ["7", "8", "9", "4", "5", "6", "1", "2", "3", ",", "0", "."];
        for (var i = 0; i < layout.Length; i++)
        {
            var symbol = layout[i];
            Put(Key(symbol, () => Add(symbol), 22, 0x2E), i / 3, i % 3);
        }

        Put(Key("±", Negate, 22, 0x22), 4, 0);
        Put(Key("⌫", Back, 22, 0x22), 4, 1);
        var ok = Key("OK", () => Committed?.Invoke(_text), 18, 0x00);
        ok.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2F, 0x9B, 0xFF));
        ok.FontWeight = FontWeight.Bold;
        Put(ok, 4, 2);

        // frosted glass: a see-through surface that fades from lighter at the top to darker at the bottom, a thin bright rim and a soft shadow
        Content = new Border
        {
            Width = 268, Padding = new Thickness(14), CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 1)],
            },
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.FromArgb(0xC8, 0x3A, 0x48, 0x66), 0), new GradientStop(Color.FromArgb(0xB4, 0x14, 0x1A, 0x28), 1)],
            },
            BoxShadow = BoxShadows.Parse("0 12 36 0 #66000000"),
            Child = new StackPanel { Children = { top, screen, keys } },
        };
        Refresh();
    }

    /// <summary>OK was tapped: the number as typed (comma or point).</summary>
    public event Action<string>? Committed;

    /// <summary>The cross was tapped.</summary>
    public event Action? Cancelled;

    private void Refresh() => _display.Text = _text.Length == 0 ? "0" : _text;

    private void Add(string symbol)
    {
        if (_fresh)
        {
            _text = string.Empty;
            _fresh = false;
        }

        // one decimal sign only
        if (symbol is "," or "." && _text.Any(c => c is ',' or '.'))
        {
            return;
        }

        _text = _text.Length == 0 && symbol is "," or "." ? "0" + symbol : _text + symbol;
        Refresh();
    }

    private void Back()
    {
        _fresh = false;
        _text = _text.Length > 0 ? _text[..^1] : _text;
        Refresh();
    }

    private void Negate()
    {
        _fresh = false;
        _text = _text.StartsWith('-') ? _text[1..] : "-" + _text;
        Refresh();
    }

    private static Button Key(string text, Action tap, double size, byte alpha)
    {
        var key = new Button
        {
            Content = text, FontSize = size, MinHeight = 54, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF)), Foreground = Brushes.White,
        };
        key.Click += (_, _) => tap();
        return key;
    }
}
