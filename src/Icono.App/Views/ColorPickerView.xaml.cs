using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;

namespace Icono.App.Views;

public partial class ColorPickerView : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPickerView),
        new FrameworkPropertyMetadata(
            Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    private double _hue;
    private double _saturation = 1;
    private double _value = 1;
    private double _alpha = 255;
    private bool _isDraggingSv;
    private bool _isSyncingFromColor;
    private bool _suppressColorCallback;

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>Raised whenever the selected color changes, regardless of source (picker drag,
    /// hue slider, hex input, or an external assignment to <see cref="SelectedColor"/>).</summary>
    public event EventHandler<Color>? ColorChanged;

    public ColorPickerView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncFromColor(SelectedColor);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ColorPickerView)d;

        if (view._suppressColorCallback)
        {
            // This assignment came from our own CommitColor() (the user dragging the hue/SV/alpha
            // controls or typing a hex value) — the sliders are already in sync (that's WHY we're
            // here), so re-syncing them would be redundant; do notify external listeners though.
            view.ColorChanged?.Invoke(view, (Color)e.NewValue);
        }
        else
        {
            // An EXTERNAL assignment — e.g. PropertiesPanelView refreshing the picker to display a
            // newly bound/selected shape's color. Sync the sliders/preview/hex to match, but this
            // is NOT a user pick, so must NOT fire ColorChanged: doing so used to re-apply this
            // "just displaying" color onto every shape in the current selection (visible as fill
            // colors silently flipping to match whenever a second shape got selected).
            view.SyncFromColor((Color)e.NewValue);
        }
    }

    private void SyncFromColor(Color color)
    {
        _isSyncingFromColor = true;

        var (h, s, v) = color.ToHsv();
        _hue = h;
        _saturation = s;
        _value = v;
        _alpha = color.A;

        HueSlider.Value = h;
        AlphaSlider.Value = color.A / 255.0 * 100;
        UpdateHueBase();
        UpdateThumbPosition();
        UpdatePreviewAndHex(color);

        _isSyncingFromColor = false;
    }

    private void UpdateHueBase() => HueBaseLayer.Background = new SolidColorBrush(ColorConversions.FromHsv(_hue, 1, 1));

    private void UpdateThumbPosition()
    {
        var width = SvCanvas.ActualWidth > 0 ? SvCanvas.ActualWidth : 180;
        var height = SvCanvas.ActualHeight > 0 ? SvCanvas.ActualHeight : 120;
        Canvas.SetLeft(SvThumb, _saturation * width - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _value) * height - SvThumb.Height / 2);
    }

    private void UpdatePreviewAndHex(Color color)
    {
        PreviewBrush.Color = color;
        HexTextBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void HueSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingFromColor)
        {
            return;
        }

        _hue = e.NewValue;
        UpdateHueBase();
        CommitColor();
    }

    private void AlphaSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AlphaValueText is not null)
        {
            AlphaValueText.Text = $"{e.NewValue:0}%";
        }

        if (_isSyncingFromColor)
        {
            return;
        }

        _alpha = e.NewValue / 100.0 * 255;
        CommitColor();
    }

    private void SvCanvas_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSv = true;
        SvCanvas.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSv)
        {
            UpdateSvFromPoint(e.GetPosition(SvCanvas));
        }
    }

    private void SvCanvas_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSv = false;
        SvCanvas.ReleaseMouseCapture();
    }

    private void UpdateSvFromPoint(Point point)
    {
        var width = SvCanvas.ActualWidth > 0 ? SvCanvas.ActualWidth : 180;
        var height = SvCanvas.ActualHeight > 0 ? SvCanvas.ActualHeight : 120;
        _saturation = Math.Clamp(point.X / width, 0, 1);
        _value = Math.Clamp(1 - point.Y / height, 0, 1);
        UpdateThumbPosition();
        CommitColor();
    }

    private void CommitColor()
    {
        var rgb = ColorConversions.FromHsv(_hue, _saturation, _value);
        var color = Color.FromArgb((byte)Math.Clamp(_alpha, 0, 255), rgb.R, rgb.G, rgb.B);
        UpdatePreviewAndHex(color);

        _suppressColorCallback = true;
        SelectedColor = color;
        _suppressColorCallback = false;
    }

    private void HexTextBox_OnLostFocus(object sender, RoutedEventArgs e) => ApplyHexInput();

    private void HexTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHexInput();
        }
    }

    private void ApplyHexInput()
    {
        var text = HexTextBox.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(text)!;
            // Hex input is RGB-only — keep whatever opacity the Alpha slider is currently set to
            // rather than snapping back to fully opaque.
            SelectedColor = Color.FromArgb((byte)Math.Clamp(_alpha, 0, 255), parsed.R, parsed.G, parsed.B);
        }
        catch
        {
            // Invalid input: leave the current color untouched.
        }
    }
}
