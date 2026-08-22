using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;
using Icono.Core;

namespace Icono.App.Views;

public partial class ToolPanelView : UserControl
{
    private const int IconsPerPage = 12;

    private EditorContext? _context;

    public EditorContext? Context
    {
        get => _context;
        set
        {
            if (_context is not null)
            {
                _context.PropertyChanged -= Context_OnPropertyChanged;
            }

            _context = value;
            IconLibrary.Context = value;

            if (_context is not null)
            {
                _context.PropertyChanged += Context_OnPropertyChanged;
            }
        }
    }

    /// <summary>Keeps the tool-panel radio buttons in sync when something OTHER than a direct
    /// click on a radio button changes the active tool — e.g. the canvas auto-reverting to Select
    /// right after a shape/text is placed. Only the Select button needs an explicit x:Name/handler:
    /// switching TO any other tool always originates from that tool's own radio button being
    /// clicked, so it's already checked by the time ActiveTool changes.</summary>
    private void Context_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorContext.ActiveTool) && Context?.ActiveTool == ActiveTool.Select)
        {
            SelectToolRadioButton.IsChecked = true;
        }
    }

    public ToolPanelView()
    {
        InitializeComponent();

        _ = new IconPager(ShapesWrapPanel, ShapesPagerDots, IconsPerPage);
        _ = new IconPager(FurnitureWrapPanel, FurniturePagerDots, IconsPerPage);
        _ = new IconPager(ArrowsWrapPanel, ArrowsPagerDots, IconsPerPage);
    }

    /// <summary>Collapses/expands the single sibling element right after a clicked card header —
    /// same pattern as PropertiesPanelView.CardHeader_OnToggle.</summary>
    private void CardHeader_OnToggle(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement header || header.Parent is not Panel parent)
        {
            return;
        }

        var index = parent.Children.IndexOf(header);
        if (index < 0 || index + 1 >= parent.Children.Count || parent.Children[index + 1] is not UIElement content)
        {
            return;
        }

        var expanding = content.Visibility != Visibility.Visible;
        content.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;

        if (header is Grid { Children.Count: > 1 } grid && grid.Children[1] is System.Windows.Shapes.Path chevron)
        {
            chevron.RenderTransform = new RotateTransform(expanding ? 0 : -90);
        }
    }

    private void ToolRadioButton_OnChecked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Context is null || sender is not RadioButton { CommandParameter: string tag })
        {
            return;
        }

        switch (tag)
        {
            case "Select":
                Context.ActiveTool = ActiveTool.Select;
                break;
            case "PanZoom":
                Context.ActiveTool = ActiveTool.PanZoom;
                break;
            case "AddPoint":
                Context.ActiveTool = ActiveTool.AddPoint;
                break;
            case "DeletePoint":
                Context.ActiveTool = ActiveTool.DeletePoint;
                break;
            case "Polyline":
            case "Curve":
                Context.ActiveTool = ActiveTool.PathDraw;
                Context.SelectedShapeKind = Enum.Parse<ShapeKind>(tag);
                break;
            default:
                Context.ActiveTool = ActiveTool.Shape;
                Context.SelectedShapeKind = Enum.Parse<ShapeKind>(tag);
                break;
        }
    }
}
