using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ArgbColor = Icono.Core.ArgbColor;

namespace Icono.Mobile.Views;

/// <summary>A colour palette for the finger: a square for saturation (across) and brightness (up), a bar for the hue, the colour as #RRGGBB and a
/// preview of the old and the new colour. Any colour of the 16 million can be chosen.</summary>
public sealed class ColorPickerPad : UserControl
{
    private double _hue;
    private double _saturation;
    private double _value;
    private readonly SquareSurface _square;
    private readonly HueSurface _bar;
    private readonly Border _newColour = new() { CornerRadius = new CornerRadius(0, 8, 8, 0) };
    private readonly TextBox _hex;
    private bool _changing;

    public ColorPickerPad(ArgbColor start)
    {
        Start = start;
        (_hue, _saturation, _value) = ToHsv(start);
        _square = new SquareSurface(this) { Height = 190, MinWidth = 280 };
        _bar = new HueSurface(this) { Height = 34, Margin = new Thickness(0, 10, 0, 0) };

        _hex = new TextBox
        {
            FontSize = 14, MinHeight = 38, Padding = new Thickness(8, 4), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
        };
        _hex.TextChanged += (_, _) => OnHexTyped();

        var oldColour = new Border { Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(start.R, start.G, start.B)), CornerRadius = new CornerRadius(8, 0, 0, 8) };
        var preview = new UniformGrid { Columns = 2, Height = 30, Margin = new Thickness(0, 10, 0, 0), Children = { oldColour, _newColour } };

        Content = new StackPanel { Width = 290, Margin = new Thickness(6), Children = { _square, _bar, preview, _hex } };
        Refresh();
    }

    /// <summary>The colour the palette was opened with.</summary>
    public ArgbColor Start { get; }

    /// <summary>The colour chosen now.</summary>
    public ArgbColor Chosen => FromHsv(_hue, _saturation, _value);

    public event EventHandler? ColorChanged;

    private void Refresh()
    {
        _changing = true;
        var c = Chosen;
        _newColour.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(c.R, c.G, c.B));
        var text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (_hex.Text != text)
        {
            _hex.Text = text;
        }

        _square.InvalidateVisual();
        _bar.InvalidateVisual();
        _changing = false;
    }

    private void OnHexTyped()
    {
        if (_changing)
        {
            return;
        }

        var text = (_hex.Text ?? string.Empty).Trim().TrimStart('#');
        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            (_hue, _saturation, _value) = ToHsv(ArgbColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            _changing = true;
            _square.InvalidateVisual();
            _bar.InvalidateVisual();
            var c = Chosen;
            _newColour.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(c.R, c.G, c.B));
            _changing = false;
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Changed()
    {
        Refresh();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- colour maths

    internal static (double H, double S, double V) ToHsv(ArgbColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double h = 0;
        if (delta > 1e-9)
        {
            h = max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            if (h < 0)
            {
                h += 360;
            }
        }

        return (h, max <= 1e-9 ? 0 : delta / max, max);
    }

    internal static ArgbColor FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        byte Byte(double value) => (byte)Math.Round(Math.Clamp((value + m) * 255, 0, 255));
        return ArgbColor.FromRgb(Byte(r), Byte(g), Byte(b));
    }

    // ---------------------------------------------------------------- the two surfaces

    private sealed class SquareSurface(ColorPickerPad pad) : Control
    {
        public override void Render(DrawingContext context)
        {
            var rect = new Rect(Bounds.Size);
            var hue = FromHsv(pad._hue, 1, 1);
            using (context.PushClip(rect))
            {
                context.DrawRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(Avalonia.Media.Color.FromRgb(hue.R, hue.G, hue.B), 1)],
                }, null, rect, 8, 8);
                context.DrawRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 1)],
                }, null, rect, 8, 8);
            }

            var at = new Point(pad._saturation * rect.Width, (1 - pad._value) * rect.Height);
            context.DrawEllipse(null, new Pen(Brushes.Black, 4), at, 11, 11);
            context.DrawEllipse(null, new Pen(Brushes.White, 2.5), at, 11, 11);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            e.Pointer.Capture(this);
            Move(e);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (e.Pointer.Captured == this)
            {
                Move(e);
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) => e.Pointer.Capture(null);

        private void Move(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            pad._saturation = Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1);
            pad._value = 1 - Math.Clamp(p.Y / Math.Max(1, Bounds.Height), 0, 1);
            pad.Changed();
        }
    }

    private sealed class HueSurface(ColorPickerPad pad) : Control
    {
        private static readonly LinearGradientBrush Rainbow = new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Avalonia.Media.Color.FromRgb(255, 0, 0), 0), new GradientStop(Avalonia.Media.Color.FromRgb(255, 255, 0), 1 / 6.0),
                new GradientStop(Avalonia.Media.Color.FromRgb(0, 255, 0), 2 / 6.0), new GradientStop(Avalonia.Media.Color.FromRgb(0, 255, 255), 3 / 6.0),
                new GradientStop(Avalonia.Media.Color.FromRgb(0, 0, 255), 4 / 6.0), new GradientStop(Avalonia.Media.Color.FromRgb(255, 0, 255), 5 / 6.0),
                new GradientStop(Avalonia.Media.Color.FromRgb(255, 0, 0), 1),
            ],
        };

        public override void Render(DrawingContext context)
        {
            var rect = new Rect(Bounds.Size);
            context.DrawRectangle(Rainbow, null, rect, 8, 8);
            var at = new Point(pad._hue / 360 * rect.Width, rect.Height / 2);
            context.DrawEllipse(null, new Pen(Brushes.Black, 4), at, 12, 12);
            context.DrawEllipse(null, new Pen(Brushes.White, 2.5), at, 12, 12);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            e.Pointer.Capture(this);
            Move(e);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (e.Pointer.Captured == this)
            {
                Move(e);
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) => e.Pointer.Capture(null);

        private void Move(PointerEventArgs e)
        {
            pad._hue = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1) * 359.999;
            pad.Changed();
        }
    }
}
