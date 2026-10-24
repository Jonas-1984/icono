using System.Windows;
using System.Windows.Controls;

namespace Icono.App.Views;

public partial class StatusBarView : UserControl
{
    public static readonly DependencyProperty ZoomValueProperty = DependencyProperty.Register(
        nameof(ZoomValue), typeof(double), typeof(StatusBarView),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double ZoomValue
    {
        get => (double)GetValue(ZoomValueProperty);
        set => SetValue(ZoomValueProperty, value);
    }

    public StatusBarView()
    {
        InitializeComponent();
    }

    /// <summary>Sets the zoom (clamped to the slider range) — used for the initial "fit page to window" zoom.</summary>
    public void SetZoom(double zoom) => ZoomSlider.Value = Math.Clamp(zoom, ZoomSlider.Minimum, ZoomSlider.Maximum);

    public double Zoom => ZoomSlider.Value;

    /// <summary>Shows the drawing scale of the document (1:N) at the bottom of the window.</summary>
    public void SetScale(double denominator) =>
        ScaleText.Text = "Scale " + Icono.Rendering.DrawingScale.Label(denominator);

    private readonly System.Windows.Threading.DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    /// <summary>Shows a short confirmation (saved, exported, ...) at the right end of the status bar; it fades out by itself.</summary>
    public void ShowMessage(string text)
    {
        StatusMessageText.BeginAnimation(OpacityProperty, null);
        StatusMessageText.Text = text;
        StatusMessageText.Opacity = 1;

        _messageTimer.Stop();
        _messageTimer.Tick -= MessageTimer_OnTick;
        _messageTimer.Tick += MessageTimer_OnTick;
        _messageTimer.Start();
    }

    private void MessageTimer_OnTick(object? sender, EventArgs e)
    {
        _messageTimer.Stop();
        StatusMessageText.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
    }

    /// <summary>Zooms by a fixed factor (menu / Ctrl+plus, Ctrl+minus).</summary>
    public void ZoomBy(double factor) => SetZoom(Math.Round(ZoomSlider.Value * factor, 2));

    private void ZoomSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ZoomValue = e.NewValue;

        if (ZoomLabel is not null)
        {
            ZoomLabel.Text = $"{e.NewValue * 100:0}%";
        }
    }
}
