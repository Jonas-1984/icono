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

    private void ZoomSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ZoomValue = e.NewValue;

        if (ZoomLabel is not null)
        {
            ZoomLabel.Text = $"{e.NewValue * 100:0}%";
        }
    }
}
