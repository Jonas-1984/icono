using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Icono.Mobile.Views;

/// <summary>A small frosted-glass pad for one length: the title, a number box (a tap opens the number pad) with its unit, a slider from 0 to 20 and OK. It opens
/// right at the place it is about - the corner of the Curve tool - instead of a wide dialog at the top of the screen. OK hands the number to <c>commit</c>;
/// closing the pad any other way hands null (nothing is changed).</summary>
public sealed class CurvePad : UserControl
{
    private const double SliderMax = 20;

    private readonly TextBox _box;
    private readonly Slider _slider;
    private readonly string _unit;
    private double _value;
    private bool _syncing;

    public CurvePad(string title, string unit, double current, double maximum)
    {
        _unit = unit;
        _value = Math.Clamp(current, 0, maximum);

        var heading = new TextBlock
        {
            Text = title, FontSize = 12.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
        };

        // the number: read-only, a tap opens the number pad
        _box = new TextBox
        {
            Text = Format(_value), FontSize = 15, MinHeight = 32, Padding = new Thickness(6, 0), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, IsReadOnly = true, Focusable = false, Classes = { "field" }, Width = 92,
        };
        _box.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            e.Handled = true;
            var pad = new NumberPad(title, unit, Format(_value));
            var flyout = new Flyout { Content = pad, Placement = PlacementMode.BottomEdgeAlignedLeft };
            flyout.FlyoutPresenterClasses.Add("numpad");
            pad.Committed += text =>
            {
                flyout.Hide();
                if (double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
                {
                    Set(Math.Clamp(typed, 0, maximum));
                }
            };
            pad.Cancelled += flyout.Hide;
            flyout.ShowAt(_box);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _slider = new Slider
        {
            Minimum = 0, Maximum = SliderMax, Value = Math.Min(_value, SliderMax), TickFrequency = 0.1, IsSnapToTickEnabled = true, Classes = { "thin" },
            Margin = new Thickness(0, 4, 0, 0),
        };
        _slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_syncing)
            {
                Set(Math.Round(_slider.Value, 1), fromSlider: true);
            }
        };

        var ok = new Button { Content = "OK", FontSize = 14, FontWeight = FontWeight.Bold, MinHeight = 32, MinWidth = 64, Padding = new Thickness(0), Classes = { "padKey", "ok" },
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ok.Click += (_, _) => Committed?.Invoke(_value);
        var close = new Button { Content = "✕", FontSize = 12, Width = 28, Height = 28, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(14), Classes = { "padKey", "soft" } };
        close.Click += (_, _) => Cancelled?.Invoke();

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(heading);
        Grid.SetColumn(close, 1);
        top.Children.Add(close);

        var unitText = new TextBlock
        {
            Text = unit, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        row.Children.Add(_box);
        Grid.SetColumn(unitText, 1);
        row.Children.Add(unitText);
        Grid.SetColumn(ok, 3);
        row.Children.Add(ok);

        // frosted glass like the number pad
        Content = new Border
        {
            Width = 236, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 1)],
            },
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.FromArgb(0xD0, 0x3A, 0x48, 0x66), 0), new GradientStop(Color.FromArgb(0xC0, 0x14, 0x1A, 0x28), 1)],
            },
            BoxShadow = BoxShadows.Parse("0 10 30 0 #66000000"),
            Child = new StackPanel { Children = { top, row, _slider } },
        };
    }

    /// <summary>OK was tapped: the number.</summary>
    public event Action<double>? Committed;

    /// <summary>The cross was tapped.</summary>
    public event Action? Cancelled;

    private string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void Set(double value, bool fromSlider = false)
    {
        _value = value;
        _box.Text = Format(value);
        if (!fromSlider)
        {
            _syncing = true;
            _slider.Value = Math.Min(value, SliderMax);
            _syncing = false;
        }
    }
}
