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

    // Same soft pastel spread as PropertiesPanelView's fill swatches, so the Paint Bucket's
    // quick-pick palette reads consistently with the rest of the app.
    private static readonly Color[] PaintBucketSwatchColors =
    [
        Colors.White, Color.FromRgb(0x3A, 0x3A, 0x3A), Color.FromRgb(0xD8, 0xDC, 0xE0),
        Color.FromRgb(0x9E, 0xD9, 0xD1), Color.FromRgb(0xA9, 0xD6, 0xF5), Color.FromRgb(0x8F, 0xB8, 0xE0),
        Color.FromRgb(0xF3, 0xA9, 0xA5), Color.FromRgb(0xF7, 0xC9, 0x8E), Color.FromRgb(0xF3, 0xE0, 0x8C),
        Color.FromRgb(0xB8, 0xDD, 0xA8), Color.FromRgb(0xCB, 0xB3, 0xE0), Color.FromRgb(0x9C, 0xD6, 0xD9),
    ];

    private EditorContext? _context;

    public EditorContext? Context
    {
        get => _context;
        set
        {
            _context = value;
            IconLibrary.Context = value;
        }
    }

    public ToolPanelView()
    {
        InitializeComponent();

        _ = new IconPager(ShapesWrapPanel, ShapesPagerDots, IconsPerPage);
        _ = new IconPager(ArrowsWrapPanel, ArrowsPagerDots, IconsPerPage);

        PaintBucketSwatchList.ItemsSource = PaintBucketSwatchColors.Select(c => (Brush)new SolidColorBrush(c)).ToList();
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
            case "PaintBucket":
                Context.ActiveTool = ActiveTool.PaintBucket;
                PaintBucketColorPopup.IsOpen = true;
                break;
            case "Border":
                // The Border tool's exact meaning (frame-around-canvas vs. a shape kind) is
                // ambiguous — treated as a Select alias for now, pending clarification.
                Context.ActiveTool = ActiveTool.Select;
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

    private void PaintBucketSwatch_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Context is null || sender is not Border { Background: SolidColorBrush brush })
        {
            return;
        }

        Context.CurrentFillColor = brush.Color.FromMediaColor();
        PaintBucketColorPopup.IsOpen = false;
    }

    private void UnionButton_OnClick(object sender, RoutedEventArgs e) => Context?.UnionSelection();

    private void TrimButton_OnClick(object sender, RoutedEventArgs e) => Context?.TrimSelection();
}
