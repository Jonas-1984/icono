using System.Windows.Media;
using Icono.App.State;
using Icono.Core;

namespace Icono.App.Views;

/// <summary>Wraps a Core <see cref="Shape"/> with WPF-only display data for the Layers panel's
/// per-shape list, keeping <see cref="Icono.Core"/> free of any WPF/UI dependency.</summary>
public sealed class ShapeListItem
{
    public Shape Shape { get; }
    public Layer Layer { get; }
    public Brush FillBrush { get; }
    public string DisplayName { get; }

    public ShapeListItem(Shape shape, Layer layer, int indexInLayer)
    {
        Shape = shape;
        Layer = layer;
        FillBrush = new SolidColorBrush(shape.FillColor.ToMediaColor());
        DisplayName = $"{shape.Kind} {indexInLayer + 1}";
    }
}
