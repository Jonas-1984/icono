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
                SyncGridPaintColor();
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

        if (e.PropertyName == nameof(EditorContext.CurrentFillColor))
        {
            SyncGridPaintColor();
        }
    }

    // ----- Design grid paint colour --------------------------------------------------------------

    private static readonly Color[] GridPalette =
    [
        Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE0, 0xE3, 0xE8), Color.FromRgb(0xB0, 0xB6, 0xC0),
        Color.FromRgb(0x7A, 0x80, 0x8C), Color.FromRgb(0x3A, 0x3E, 0x47), Color.FromRgb(0x00, 0x00, 0x00),

        Color.FromRgb(0xE5, 0x39, 0x35), Color.FromRgb(0xF4, 0x51, 0x1E), Color.FromRgb(0xFB, 0x8C, 0x00),
        Color.FromRgb(0xFD, 0xD8, 0x35), Color.FromRgb(0xC0, 0xCA, 0x33), Color.FromRgb(0x43, 0xA0, 0x47),

        Color.FromRgb(0x00, 0x89, 0x7B), Color.FromRgb(0x00, 0xAC, 0xC1), Color.FromRgb(0x03, 0xA9, 0xF4),
        Color.FromRgb(0x1E, 0x88, 0xE5), Color.FromRgb(0x39, 0x49, 0xAB), Color.FromRgb(0x7E, 0x57, 0xC2),

        Color.FromRgb(0xAB, 0x47, 0xBC), Color.FromRgb(0xEC, 0x40, 0x7A), Color.FromRgb(0xF0, 0x62, 0x92),
        Color.FromRgb(0xF3, 0xA9, 0xA5), Color.FromRgb(0xF7, 0xC9, 0x8E), Color.FromRgb(0xF3, 0xE0, 0x8C),

        Color.FromRgb(0xB8, 0xDD, 0xA8), Color.FromRgb(0x9E, 0xD9, 0xD1), Color.FromRgb(0xA9, 0xD6, 0xF5),
        Color.FromRgb(0x8F, 0xB8, 0xE0), Color.FromRgb(0xCB, 0xB3, 0xE0), Color.FromRgb(0x8D, 0x6E, 0x63),
    ];

    private bool _isSyncingGridColor;

    private void InitializeGridPalette()
    {
        GridPaintSwatchList.ItemsSource = GridPalette.Select(c => (Brush)new SolidColorBrush(c)).ToList();
        GridPaintColorPicker.ColorChanged += GridPaintColorPicker_OnColorChanged;
    }

    /// <summary>Mirrors <see cref="EditorContext.CurrentFillColor"/> (the colour the bucket tool paints with) in the chip,
    /// the hex label and the custom-colour picker.</summary>
    private void SyncGridPaintColor()
    {
        if (Context is null)
        {
            return;
        }

        var color = Context.CurrentFillColor.ToMediaColor();
        GridPaintColorChip.Background = new SolidColorBrush(color);
        GridPaintColorText.Text = color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        _isSyncingGridColor = true;
        try
        {
            GridPaintColorPicker.SelectedColor = color;
        }
        finally
        {
            _isSyncingGridColor = false;
        }
    }

    private void GridPaintSwatch_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Context is null || sender is not Border { Background: SolidColorBrush brush })
        {
            return;
        }

        Context.CurrentFillColor = brush.Color.FromMediaColor();
        SyncGridPaintColor();

        // Choosing a paint colour here means "I want to paint": switch to the bucket straight away.
        GridBucketRadioButton.IsChecked = true;
        e.Handled = true;
    }

    private void GridPaintColorPicker_OnColorChanged(object? sender, Color color)
    {
        if (Context is null || _isSyncingGridColor)
        {
            return;
        }

        Context.CurrentFillColor = color.FromMediaColor();
        GridPaintColorChip.Background = new SolidColorBrush(color);
        GridPaintColorText.Text = color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void GridCustomColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        SyncGridPaintColor();
        GridColorPopup.IsOpen = true;
    }

    public ToolPanelView()
    {
        InitializeComponent();
        InitializeGridPalette();

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
            case "RegionColor":
                Context.ActiveTool = ActiveTool.RegionColor;
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
