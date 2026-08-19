using System.Collections.ObjectModel;
using System.Windows.Media;
using Icono.App.State;
using Icono.Core;

namespace Icono.App.Views;

/// <summary>Wraps a Core <see cref="Layer"/> with a WPF-only display brush for the Layers list,
/// keeping <see cref="Icono.Core"/> free of any WPF dependency.</summary>
public sealed class LayerListItem
{
    public Layer Layer { get; }
    public Brush AccentBrush { get; }

    /// <summary>Individual shapes in this layer, so one can be selected regardless of where it
    /// sits on the canvas (including off-canvas). Auto-refreshes whenever the layer's shapes
    /// collection changes — no manual rebuild calls needed at each mutation site.</summary>
    public ObservableCollection<ShapeListItem> ShapeItems { get; } = new();

    public LayerListItem(Layer layer)
    {
        Layer = layer;
        AccentBrush = new SolidColorBrush(layer.AccentColor.ToMediaColor());

        RebuildShapeItems();
        layer.Shapes.CollectionChanged += (_, _) => RebuildShapeItems();
    }

    private void RebuildShapeItems()
    {
        ShapeItems.Clear();

        for (var i = 0; i < Layer.Shapes.Count; i++)
        {
            ShapeItems.Add(new ShapeListItem(Layer.Shapes[i], Layer, i));
        }
    }
}
