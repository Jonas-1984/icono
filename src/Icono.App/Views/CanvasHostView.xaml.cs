using System.Windows;
using System.Windows.Controls;
using Icono.Rendering;
using SkiaSharp.Views.Desktop;

namespace Icono.App.Views;

public partial class CanvasHostView : UserControl
{
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(CanvasHostView),
        new PropertyMetadata(1.0, OnZoomChanged));

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public CanvasHostView()
    {
        InitializeComponent();
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (CanvasHostView)d;
        var zoom = (double)e.NewValue;
        view.ZoomTransform.ScaleX = zoom;
        view.ZoomTransform.ScaleY = zoom;
    }

    private void SkiaCanvas_OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        CheckerboardRenderer.Render(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }
}
