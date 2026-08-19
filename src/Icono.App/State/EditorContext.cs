using System.Collections.ObjectModel;
using Icono.Core;

namespace Icono.App.State;

public enum ActiveTool
{
    Select,
    PanZoom,
    Shape,
    PaintBucket,
}

/// <summary>
/// Shared editor state — one instance created by MainWindow and handed to the Tools panel,
/// canvas, and Design panel. Plain object, no DI/MVVM framework: matches this project's
/// existing non-MVVM code-behind style.
/// </summary>
public sealed class EditorContext : ObservableObject
{
    private ActiveTool _activeTool = ActiveTool.Select;
    private ShapeKind _selectedShapeKind = ShapeKind.Rectangle;
    private Layer? _selectedLayer;
    private ArgbColor _currentFillColor = ArgbColor.FromRgb(0x2E, 0x86, 0xFF);

    public Document Document { get; } = new();

    public ActiveTool ActiveTool { get => _activeTool; set => SetField(ref _activeTool, value); }

    public ShapeKind SelectedShapeKind { get => _selectedShapeKind; set => SetField(ref _selectedShapeKind, value); }

    /// <summary>Source of truth for selection. Index order has no meaning; multi-select is a set.</summary>
    public ObservableCollection<Shape> SelectedShapes { get; } = new();

    /// <summary>The last-touched selected shape, or null if nothing is selected. Setting it
    /// replaces the whole selection with just this one shape (or clears it, if null).</summary>
    public Shape? SelectedShape
    {
        get => SelectedShapes.Count > 0 ? SelectedShapes[^1] : null;
        set
        {
            SelectedShapes.Clear();
            if (value is not null)
            {
                SelectedShapes.Add(value);
            }
        }
    }

    public Layer? SelectedLayer { get => _selectedLayer; set => SetField(ref _selectedLayer, value); }

    public ArgbColor CurrentFillColor { get => _currentFillColor; set => SetField(ref _currentFillColor, value); }

    public event EventHandler? Invalidated;

    public EditorContext()
    {
        SelectedShapes.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(SelectedShape));
    }

    public void RequestRedraw() => Invalidated?.Invoke(this, EventArgs.Empty);
}
