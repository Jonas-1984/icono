using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;

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
                _context.ObjectSnap.PropertyChanged -= ObjectSnap_OnPropertyChanged;
                _context.PointSnap.PropertyChanged -= PointSnap_OnPropertyChanged;
            }

            _context = value;
            IconLibrary.Context = value;

            if (_context is not null)
            {
                _context.PropertyChanged += Context_OnPropertyChanged;
                SyncGridPaintColor();
                _context.ObjectSnap.PropertyChanged += ObjectSnap_OnPropertyChanged;
                SyncObjectSnapChecks();
                _context.PointSnap.PropertyChanged += PointSnap_OnPropertyChanged;
                SyncPointSnapChecks();
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
        if (e.PropertyName is nameof(EditorContext.ActiveTool) or nameof(EditorContext.SelectedShapeKind))
        {
            SyncToolRadios();
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

    /// <summary>The design-grid / image-import card, so the Insert menu can reuse its import dialogs.</summary>
    public IconLibraryView ImportLibrary => IconLibrary;

    /// <summary>Checks the radio button that belongs to the active tool (and the selected shape kind), so the palette always shows
    /// what is really active — also when the tool was changed from a menu, a shortcut or by the canvas after placing a shape.</summary>
    private void SyncToolRadios()
    {
        if (Context is null)
        {
            return;
        }

        var tag = Context.ActiveToolTag;

        foreach (var radio in FindRadios(this))
        {
            var isMatch = tag is not null && radio.CommandParameter is string parameter && parameter == tag;
            if (radio.IsChecked != isMatch && (isMatch || radio.IsChecked == true))
            {
                radio.IsChecked = isMatch;
            }
        }
    }

    private static IEnumerable<RadioButton> FindRadios(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is RadioButton { GroupName: "ToolSelection" } radio)
            {
                yield return radio;
            }

            foreach (var nested in FindRadios(child))
            {
                yield return nested;
            }
        }
    }

    public ToolPanelView()
    {
        InitializeComponent();
        BuildPointSnapList();
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

    private void WallSnapCheckBox_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Context is null || sender is not CheckBox { Tag: string tag } box)
        {
            return;
        }

        var on = box.IsChecked == true;
        var options = Context.WallSnap;
        switch (tag)
        {
            case "Enabled": options.Enabled = on; break;
            case "Axis": options.Axis = on; break;
            case "Align": options.Align = on; break;
            case "EqualLength": options.EqualLength = on; break;
            case "Parallel": options.Parallel = on; break;
        }
    }

    // ----- Object snap (end points, middles, centres, crossings ...) ------------------------------------------------

    /// <summary>The kinds in the card: name, a small drawing of the mark that appears on the canvas, and what it does.</summary>
    private static readonly (PointSnapKind Kind, string Label, string Icon, string Tip)[] PointSnapRows =
    [
        (PointSnapKind.Endpoint, "Endpoint", "M2,2 H12 V12 H2 Z", "The corner or end of a line, curve, outline or wall."),
        (PointSnapKind.Midpoint, "Midpoint", "M7,2 L12.5,12 H1.5 Z", "The middle of a straight line or wall face."),
        (PointSnapKind.Center, "Center", "M7,1.5 A5.5,5.5 0 1 1 6.99,1.5 Z", "The centre of a circle or an ellipse."),
        (PointSnapKind.GeometricCenter, "Geometric center", "M7,1.5 A5.5,5.5 0 1 1 6.99,1.5 Z M1.5,7 H12.5 M7,1.5 V12.5", "The centre of area of a closed shape."),
        (PointSnapKind.Node, "Node", "M7,1.5 A5.5,5.5 0 1 1 6.99,1.5 Z M4.5,4.5 L9.5,9.5 M4.5,9.5 L9.5,4.5", "A definition point: the points of a dimension, the corners of a wall's centre line."),
        (PointSnapKind.Quadrant, "Quadrant", "M7,1 L13,7 L7,13 L1,7 Z", "The points of a circle or ellipse at 0, 90, 180 and 270 degrees."),
        (PointSnapKind.Intersection, "Intersection", "M2,2 L12,12 M2,12 L12,2", "Where two lines, curves or walls cross."),
        (PointSnapKind.Extension, "Extension", "M1,7 H3.5 M6,7 H8 M10.5,7 H13", "On the imagined continuation of an open line beyond its end (a dashed line shows it)."),
        (PointSnapKind.Insertion, "Insertion", "M2,2 H12 V12 H2 Z M2,7 H12 M7,2 V12", "The insertion point of a text, an image, a door, a window or a piece of furniture."),
        (PointSnapKind.Perpendicular, "Perpendicular", "M2,2 V12 H12 M2,8 H6 V12", "The foot of the line at a right angle from the last point to an object."),
        (PointSnapKind.Tangent, "Tangent", "M7,1.5 A3.5,3.5 0 1 1 6.99,1.5 Z M1,9.5 H13", "The point of a circle or curve where a line from the last point just touches it."),
        (PointSnapKind.Nearest, "Nearest", "M2,2 H12 L2,12 H12 Z", "The nearest point on an object (anywhere along its edge)."),
        (PointSnapKind.ApparentIntersection, "Apparent intersection", "M4,4 L10,10 M4,10 L10,4 M1.5,1.5 H12.5 V12.5 H1.5 Z", "Where the continuations of two lines meet although the lines themselves do not touch."),
        (PointSnapKind.Parallel, "Parallel", "M2,9 L8,3 M6,12 L12,6", "On the line from the last point that runs parallel to another line."),
    ];

    /// <summary>Opens the OBJECT SNAP card and brings it into view (View menu).</summary>
    public void ShowPointSnapCard()
    {
        if (PointSnapList.Parent is FrameworkElement content)
        {
            content.Visibility = Visibility.Visible;
        }

        PointSnapCard.BringIntoView();
    }

    private void BuildPointSnapList()
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
        foreach (var (kind, label, icon, tip) in PointSnapRows)
        {
            var name = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(icon), Stroke = accent, StrokeThickness = 1.3, Width = 14, Height = 14, Stretch = Stretch.None,
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(name);

            var box = new CheckBox { Content = row, Tag = kind, ToolTip = tip, Margin = new Thickness(12, 2, 0, 2) };
            box.Click += PointSnapCheckBox_OnClick;
            PointSnapList.Children.Add(box);
        }
    }

    private void PointSnapCheckBox_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Context is null || sender is not CheckBox box)
        {
            return;
        }

        var on = box.IsChecked == true;
        if (box.Tag is PointSnapKind kind)
        {
            Context.PointSnap.Set(kind, on);
        }
        else
        {
            Context.PointSnap.Enabled = on;
        }
    }

    private void PointSnap_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SyncPointSnapChecks();

    private void SyncPointSnapChecks()
    {
        if (Context is null)
        {
            return;
        }

        var options = Context.PointSnap;
        PointSnapEnabledCheckBox.IsChecked = options.Enabled;
        foreach (var box in PointSnapList.Children.OfType<CheckBox>())
        {
            if (box.Tag is PointSnapKind kind)
            {
                box.IsChecked = options.IsOn(kind);
                box.IsEnabled = options.Enabled;
            }
        }
    }

    private void ObjectSnapCheckBox_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Context is null || sender is not CheckBox { Tag: string tag } box)
        {
            return;
        }

        var on = box.IsChecked == true;
        switch (tag)
        {
            case "Enabled": Context.ObjectSnap.Enabled = on; break;
            case "Walls": Context.ObjectSnap.Walls = on; break;
            case "Objects": Context.ObjectSnap.Objects = on; break;
        }
    }

    private void ObjectSnap_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SyncObjectSnapChecks();

    /// <summary>Shows the snap settings (they can also be switched in the View menu).</summary>
    private void SyncObjectSnapChecks()
    {
        if (Context is null)
        {
            return;
        }

        var options = Context.ObjectSnap;
        ObjectSnapEnabledCheckBox.IsChecked = options.Enabled;
        ObjectSnapWallsCheckBox.IsChecked = options.Walls;
        ObjectSnapObjectsCheckBox.IsChecked = options.Objects;
        ObjectSnapWallsCheckBox.IsEnabled = ObjectSnapObjectsCheckBox.IsEnabled = options.Enabled;
    }

    private void MatchAspectCheckBox_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Context is null || sender is not CheckBox { Tag: string tag } box || !Enum.TryParse<MatchAspects>(tag, out var aspect))
        {
            return;
        }

        Context.MatchAspects = box.IsChecked == true ? Context.MatchAspects | aspect : Context.MatchAspects & ~aspect;
    }

    private void CloseRoom_OnClick(object sender, System.Windows.RoutedEventArgs e) => Context?.CloseSelectedWalls();

    private void Room_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Context is not null && sender is FrameworkElement { Tag: string tag } && Enum.TryParse<RoomPreset>(tag, out var preset))
        {
            Context.InsertRoom(preset);
        }
    }

    private void AddDimensions_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Context is not null && sender is FrameworkElement { Tag: string tag } && Enum.TryParse<DimensionSet>(tag, out var set))
        {
            Context.AddDimensionsToSelection(set);
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
            case "Eyedropper":
                Context.ActiveTool = ActiveTool.Eyedropper;
                break;
            case "WallCut":
                Context.ActiveTool = ActiveTool.WallCut;
                break;
            case "Measure":
                Context.ActiveTool = ActiveTool.Measure;
                break;
            case "MeasureGap":
                Context.ActiveTool = ActiveTool.MeasureGap;
                break;
            case "Match":
                if (Context.ActiveTool != ActiveTool.Match)
                {
                    Context.BeginMatch();
                }

                break;
            case "DimHorizontal":
            case "DimVertical":
            case "DimAligned":
            case "DimAngular":
            case "DimRadius":
            case "DimDiameter":
                Context.CurrentDimensionKind = Enum.Parse<DimensionKind>(tag[3..]);
                Context.SelectedShapeKind = ShapeKind.Dimension;
                Context.ActiveTool = ActiveTool.PathDraw;
                break;
            case "Wall":
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
