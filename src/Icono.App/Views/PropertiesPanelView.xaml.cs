using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;
using Icono.App.State.Undo;
using Icono.Core;

namespace Icono.App.Views;

public partial class PropertiesPanelView : UserControl
{
    // A soft, pastel palette (muted rather than fully-saturated) so shapes read as gentle by
    // default, matching the softer look requested for fills/borders/connectors.
    private static readonly Color[] SwatchColors =
    [
        Colors.White, Color.FromRgb(0x3A, 0x3A, 0x3A), Color.FromRgb(0xD8, 0xDC, 0xE0),
        Color.FromRgb(0x9E, 0xD9, 0xD1), Color.FromRgb(0xA9, 0xD6, 0xF5), Color.FromRgb(0x8F, 0xB8, 0xE0),
        Color.FromRgb(0xF3, 0xA9, 0xA5), Color.FromRgb(0xF7, 0xC9, 0x8E), Color.FromRgb(0xF3, 0xE0, 0x8C),
        Color.FromRgb(0xB8, 0xDD, 0xA8), Color.FromRgb(0xCB, 0xB3, 0xE0), Color.FromRgb(0x9C, 0xD6, 0xD9),
    ];

    // A broader spread of ~29 plain colors for the BORDER card's STYLE PRESETS grid (alongside
    // the named Gloss/Acrylic presets above) — covers the hue wheel plus a few neutrals.
    private static readonly (string Name, Color Color)[] PresetColors =
    [
        ("White", Colors.White), ("Black", Color.FromRgb(0x2B, 0x2B, 0x2B)), ("Gray", Color.FromRgb(0xB0, 0xB0, 0xB0)),
        ("Silver", Color.FromRgb(0xD8, 0xDC, 0xE0)), ("Red", Color.FromRgb(0xE5, 0x73, 0x73)),
        ("Deep Red", Color.FromRgb(0xC6, 0x28, 0x28)), ("Orange", Color.FromRgb(0xFF, 0xB7, 0x4D)),
        ("Deep Orange", Color.FromRgb(0xF4, 0x51, 0x1E)), ("Amber", Color.FromRgb(0xFF, 0xD5, 0x4F)),
        ("Yellow", Color.FromRgb(0xFF, 0xF1, 0x76)), ("Lime", Color.FromRgb(0xC0, 0xCA, 0x33)),
        ("Green", Color.FromRgb(0x81, 0xC7, 0x84)), ("Deep Green", Color.FromRgb(0x38, 0x8E, 0x3C)),
        ("Teal", Color.FromRgb(0x4D, 0xB6, 0xAC)), ("Deep Teal", Color.FromRgb(0x00, 0x79, 0x6B)),
        ("Cyan", Color.FromRgb(0x4D, 0xD0, 0xE1)), ("Light Blue", Color.FromRgb(0x4F, 0xC3, 0xF7)),
        ("Blue", Color.FromRgb(0x64, 0xB5, 0xF6)), ("Deep Blue", Color.FromRgb(0x1E, 0x88, 0xE5)),
        ("Indigo", Color.FromRgb(0x5C, 0x6B, 0xC0)), ("Purple", Color.FromRgb(0xBA, 0x68, 0xC8)),
        ("Deep Purple", Color.FromRgb(0x7E, 0x57, 0xC2)), ("Pink", Color.FromRgb(0xF0, 0x62, 0x92)),
        ("Rose", Color.FromRgb(0xEC, 0x40, 0x7A)), ("Brown", Color.FromRgb(0xA1, 0x88, 0x7F)),
        ("Deep Brown", Color.FromRgb(0x6D, 0x4C, 0x41)), ("Beige", Color.FromRgb(0xE6, 0xD3, 0xB3)),
        ("Mint", Color.FromRgb(0xA8, 0xE6, 0xCF)), ("Soft Navy", Color.FromRgb(0x3D, 0x6E, 0x8C)),
    ];

    private readonly ObservableCollection<LayerListItem> _layerItems = new();
    private EditorContext? _context;
    private Shape? _boundShape;
    private List<ShapeSnapshot>? _sliderUndoBefore;

    public EditorContext? Context
    {
        get => _context;
        set
        {
            if (_context is not null)
            {
                _context.PropertyChanged -= OnContextPropertyChanged;
                _context.Document.Layers.CollectionChanged -= OnDocumentLayersChanged;
            }

            _context = value;

            if (_context is not null)
            {
                _context.PropertyChanged += OnContextPropertyChanged;
                _context.Document.Layers.CollectionChanged += OnDocumentLayersChanged;
                BgTransparentRadio.IsChecked = _context.Document.BackgroundMode == BackgroundMode.Transparent;
                BgSolidRadio.IsChecked = _context.Document.BackgroundMode == BackgroundMode.SolidColor;
                BgBlurRadio.IsChecked = _context.Document.BackgroundMode == BackgroundMode.Blur;
            }

            RebuildLayerList();
            RefreshForSelection();
        }
    }

    public PropertiesPanelView()
    {
        InitializeComponent();

        SwatchList.ItemsSource = SwatchColors.Select(c => (Brush)new SolidColorBrush(c)).ToList();

        var presets = new List<BorderStylePreset>
        {
            new("Blue Gloss", (Brush)FindResource("GlossyBlueBrush")),
            new("Silver Gloss", new LinearGradientBrush(Colors.White, Color.FromRgb(0x8A, 0x8A, 0x8A), 90)),
            new("Gold Gloss", new LinearGradientBrush(Color.FromRgb(0xFF, 0xE6, 0x8A), Color.FromRgb(0xB8, 0x86, 0x0B), 90)),
            new("Red Gloss", new LinearGradientBrush(Color.FromRgb(0xFF, 0x8A, 0x80), Color.FromRgb(0x8E, 0x1B, 0x1B), 90)),
            new("Acrylic", new LinearGradientBrush(Color.FromArgb(210, 255, 255, 255), Color.FromArgb(60, 255, 255, 255), 90),
                ApplyAcrylicPreset),
        };
        presets.AddRange(PresetColors.Select(pc => new BorderStylePreset(pc.Name, new SolidColorBrush(pc.Color))));
        BorderPresetList.ItemsSource = presets;

        LayerList.ItemsSource = _layerItems;
        FillColorPicker.ColorChanged += FillColorPicker_OnColorChanged;
        BackgroundColorPicker.ColorChanged += BackgroundColorPicker_OnColorChanged;
        BorderColorPicker.ColorChanged += BorderColorPicker_OnColorChanged;
        BorderGradientColorPicker.ColorChanged += BorderGradientColorPicker_OnColorChanged;
        ShadowColorPicker.ColorChanged += ShadowColorPicker_OnColorChanged;
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorContext.SelectedShape))
        {
            RefreshForSelection();
        }
    }

    /// <summary>Keeps the layer list in sync with Document.Layers regardless of whether the
    /// change came from a UI click or an undo/redo replay.</summary>
    private void OnDocumentLayersChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildLayerList();

    private void RefreshForSelection()
    {
        var shapes = _context?.SelectedShapes;
        var count = shapes?.Count ?? 0;

        BindToSelectedShape(count > 0 ? shapes![^1] : null);

        MultiSelectHint.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var sizeEditable = count == 1;
        XTextBox.IsEnabled = sizeEditable;
        YTextBox.IsEnabled = sizeEditable;
        WidthTextBox.IsEnabled = sizeEditable;
        HeightTextBox.IsEnabled = sizeEditable;

        var offCanvas = count == 1 && _context is not null && IsOffCanvas(shapes![0], _context.Document);
        BringIntoViewButton.Visibility = offCanvas ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsOffCanvas(Shape shape, Document document) =>
        shape.X + shape.Width <= 0 || shape.Y + shape.Height <= 0 ||
        shape.X >= document.Width || shape.Y >= document.Height;

    private void BringIntoViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_boundShape is null || _context is null)
        {
            return;
        }

        _boundShape.X = Math.Max(0, (_context.Document.Width - _boundShape.Width) / 2);
        _boundShape.Y = Math.Max(0, (_context.Document.Height - _boundShape.Height) / 2);
        _context.RequestRedraw();
        RefreshForSelection();
    }

    private void BindToSelectedShape(Shape? shape)
    {
        if (_boundShape is not null)
        {
            _boundShape.PropertyChanged -= OnBoundShapePropertyChanged;
        }

        _boundShape = shape;

        if (_boundShape is not null)
        {
            _boundShape.PropertyChanged += OnBoundShapePropertyChanged;
        }

        NoSelectionHint.Visibility = shape is null ? Visibility.Visible : Visibility.Collapsed;
        RefreshControlsFromShape();
    }

    private void OnBoundShapePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshControlsFromShape();
        RefreshForSelection();
    }

    private void RefreshControlsFromShape()
    {
        var shape = _boundShape;
        if (shape is null)
        {
            return;
        }

        XTextBox.Text = shape.X.ToString("0", CultureInfo.InvariantCulture);
        YTextBox.Text = shape.Y.ToString("0", CultureInfo.InvariantCulture);
        WidthTextBox.Text = shape.Width.ToString("0", CultureInfo.InvariantCulture);
        HeightTextBox.Text = shape.Height.ToString("0", CultureInfo.InvariantCulture);
        RotationTextBox.Text = shape.Rotation.ToString("0", CultureInfo.InvariantCulture);

        Depth3DSlider.Value = shape.Depth3D;
        switch (shape.PathRoutingStyle)
        {
            case PathRoutingStyle.Orthogonal:
                RoutingOrthogonalRadio.IsChecked = true;
                break;
            case PathRoutingStyle.Rounded:
                RoutingRoundedRadio.IsChecked = true;
                break;
            case PathRoutingStyle.Curved:
                RoutingCurvedRadio.IsChecked = true;
                break;
            default:
                RoutingStraightRadio.IsChecked = true;
                break;
        }

        BorderColorPicker.SelectedColor = shape.BorderColor.ToMediaColor();
        BorderThicknessSlider.Value = shape.BorderThickness;
        BorderBlurSlider.Value = shape.BorderBlurRadius;
        BorderGradientColorPicker.SelectedColor = shape.BorderGradientColor.ToMediaColor();
        DashedCheckBox.IsChecked = shape.Dashed;
        BorderGradientColorPicker.Visibility = shape.BorderStyleKind == BorderStyleKind.Gradient
            ? Visibility.Visible
            : Visibility.Collapsed;
        switch (shape.BorderStyleKind)
        {
            case BorderStyleKind.Gradient:
                BorderGradientRadio.IsChecked = true;
                break;
            case BorderStyleKind.Fade:
                BorderFadeRadio.IsChecked = true;
                break;
            default:
                BorderSolidRadio.IsChecked = true;
                break;
        }

        CornerRadiusSlider.Value = shape.CornerRadius;
        OpacitySlider.Value = shape.Opacity;
        BlurSlider.Value = shape.BlurRadius;
        FadeSlider.Value = shape.FadeAmount;
        ShadowCheckBox.IsChecked = shape.ShadowEnabled;
        ShadowDistanceSlider.Value = shape.ShadowDistance;
        ShadowStrengthSlider.Value = shape.ShadowStrength;
        ShadowAngleSlider.Value = shape.ShadowAngle;
        ShadowColorPicker.SelectedColor = shape.ShadowColor.ToMediaColor();

        FillColorPicker.SelectedColor = shape.FillColor.ToMediaColor();

        TextBoldToggle.IsChecked = shape.TextBold;
        TextItalicToggle.IsChecked = shape.TextItalic;
    }

    /// <summary>Applies an edit to every currently selected shape (or a no-op if none).</summary>
    private void ApplyToSelection(Action<Shape> apply)
    {
        if (_context is null)
        {
            return;
        }

        foreach (var shape in _context.SelectedShapes)
        {
            apply(shape);
        }

        _context.RequestRedraw();
    }

    /// <summary>Like <see cref="ApplyToSelection"/>, but pushes a single undo entry for the edit.
    /// Use for discrete one-shot edits (swatch clicks, checkboxes) — continuous drags (sliders)
    /// instead bracket themselves with <see cref="EffectSlider_OnDragStart"/>/<see cref="EffectSlider_OnDragEnd"/>
    /// so one gesture yields one undo entry, not one per intermediate value.</summary>
    private void ApplyToSelectionWithUndo(Action<Shape> apply)
    {
        if (_context is null || _context.SelectedShapes.Count == 0)
        {
            return;
        }

        var shapes = _context.SelectedShapes.ToList();
        var before = shapes.Select(ShapeSnapshot.Capture).ToList();

        foreach (var shape in shapes)
        {
            apply(shape);
        }

        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        _context.UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, before, after));
        _context.RequestRedraw();
    }

    private void EffectSlider_OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _sliderUndoBefore = _context?.SelectedShapes.Select(ShapeSnapshot.Capture).ToList();
    }

    private void EffectSlider_OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_sliderUndoBefore is null || _context is null || _sliderUndoBefore.Count != _context.SelectedShapes.Count)
        {
            _sliderUndoBefore = null;
            return;
        }

        var shapes = _context.SelectedShapes.ToList();
        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        if (!after.SequenceEqual(_sliderUndoBefore))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, _sliderUndoBefore, after));
        }

        _sliderUndoBefore = null;
    }

    private void FillColorPicker_OnColorChanged(object? sender, Color color)
    {
        if (_context is null)
        {
            return;
        }

        _context.CurrentFillColor = color.FromMediaColor();
        ApplyToSelectionWithUndo(s => s.FillColor = color.FromMediaColor());
    }

    private void FillSwatch_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_context is null || sender is not Border { Background: SolidColorBrush brush })
        {
            return;
        }

        _context.CurrentFillColor = brush.Color.FromMediaColor();
        ApplyToSelectionWithUndo(s => s.FillColor = brush.Color.FromMediaColor());
    }

    private void BorderPreset_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: BorderStylePreset preset })
        {
            return;
        }

        if (preset.CustomApply is not null)
        {
            ApplyToSelectionWithUndo(preset.CustomApply);
            RefreshControlsFromShape();
            return;
        }

        var color = preset.Preview switch
        {
            SolidColorBrush solid => solid.Color,
            LinearGradientBrush { GradientStops.Count: > 0 } gradient => gradient.GradientStops[0].Color,
            _ => Colors.Gray,
        };

        ApplyToSelectionWithUndo(s => s.BorderColor = color.FromMediaColor());
    }

    /// <summary>Frosted-glass "Acrylic/Crystal" look: a light translucent fill, a soft blur, a
    /// subtle top-to-bottom fade for the highlight, and a light gradient border rim.</summary>
    private static void ApplyAcrylicPreset(Shape shape)
    {
        shape.FillColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
        shape.Opacity = 55;
        shape.BlurRadius = 3;
        shape.FadeAmount = 25;
        shape.BorderStyleKind = BorderStyleKind.Gradient;
        shape.BorderColor = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);
        shape.BorderGradientColor = ArgbColor.FromRgb(0xC8, 0xE0, 0xFF);
        shape.BorderBlurRadius = 2;
    }

    private void SizeTextBox_OnLostFocus(object sender, RoutedEventArgs e) => ApplySizeTextBox(sender);

    private void SizeTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySizeTextBox(sender);
        }
    }

    private void ApplySizeTextBox(object sender)
    {
        if (_boundShape is null || sender is not TextBox { Tag: string field } textBox)
        {
            return;
        }

        if (!double.TryParse(textBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            RefreshControlsFromShape();
            return;
        }

        value = Math.Max(0, value);

        var before = ShapeSnapshot.Capture(_boundShape);

        switch (field)
        {
            case "X": _boundShape.X = value; break;
            case "Y": _boundShape.Y = value; break;
            case "Width": _boundShape.Width = Math.Max(1, value); break;
            case "Height": _boundShape.Height = Math.Max(1, value); break;
        }

        var after = ShapeSnapshot.Capture(_boundShape);
        if (_context is not null && !after.Equals(before))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([_boundShape], [before], [after]));
        }

        _context?.RequestRedraw();
    }

    private void RotationTextBox_OnLostFocus(object sender, RoutedEventArgs e) => ApplyRotationTextBox();

    private void RotationTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyRotationTextBox();
        }
    }

    private void ApplyRotationTextBox()
    {
        if (_boundShape is null)
        {
            return;
        }

        if (!double.TryParse(RotationTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            RefreshControlsFromShape();
            return;
        }

        var before = ShapeSnapshot.Capture(_boundShape);
        _boundShape.Rotation = value;
        var after = ShapeSnapshot.Capture(_boundShape);

        if (_context is not null && !after.Equals(before))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([_boundShape], [before], [after]));
        }

        _context?.RequestRedraw();
    }

    private void RoutingStyleRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        var style = Enum.Parse<PathRoutingStyle>(tag);
        ApplyToSelectionWithUndo(s => s.PathRoutingStyle = style);
    }

    private void Depth3DSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Depth3DValueText is not null)
        {
            Depth3DValueText.Text = $"{e.NewValue:0}px";
        }

        ApplyToSelection(s => s.Depth3D = e.NewValue);
    }

    private void FlipHorizontalButton_OnClick(object sender, RoutedEventArgs e) =>
        ApplyToSelectionWithUndo(s => s.FlipHorizontal = !s.FlipHorizontal);

    private void FlipVerticalButton_OnClick(object sender, RoutedEventArgs e) =>
        ApplyToSelectionWithUndo(s => s.FlipVertical = !s.FlipVertical);

    private void BorderThicknessSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BorderThicknessValueText is not null)
        {
            BorderThicknessValueText.Text = $"{e.NewValue:0}px";
        }

        ApplyToSelection(s => s.BorderThickness = e.NewValue);
    }

    private void BorderStyleRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        var style = Enum.Parse<BorderStyleKind>(tag);
        BorderGradientColorPicker.Visibility = style == BorderStyleKind.Gradient ? Visibility.Visible : Visibility.Collapsed;
        ApplyToSelectionWithUndo(s => s.BorderStyleKind = style);
    }

    private void BorderColorPicker_OnColorChanged(object? sender, Color color) =>
        ApplyToSelectionWithUndo(s => s.BorderColor = color.FromMediaColor());

    private void BorderGradientColorPicker_OnColorChanged(object? sender, Color color) =>
        ApplyToSelectionWithUndo(s => s.BorderGradientColor = color.FromMediaColor());

    private void BorderBlurSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BorderBlurValueText is not null)
        {
            BorderBlurValueText.Text = $"{e.NewValue:0}px";
        }

        ApplyToSelection(s => s.BorderBlurRadius = e.NewValue);
    }

    private void DashedCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var dashed = DashedCheckBox.IsChecked == true;
        ApplyToSelectionWithUndo(s => s.Dashed = dashed);
    }

    private void CornerRadiusSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CornerRadiusValueText is not null)
        {
            CornerRadiusValueText.Text = $"{e.NewValue:0}px";
        }

        ApplyToSelection(s => s.CornerRadius = e.NewValue);
    }

    private void OpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueText is not null)
        {
            OpacityValueText.Text = $"{e.NewValue:0}%";
        }

        ApplyToSelection(s => s.Opacity = e.NewValue);
    }

    private void BlurSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BlurValueText is not null)
        {
            BlurValueText.Text = $"{e.NewValue:0}px";
        }

        ApplyToSelection(s => s.BlurRadius = e.NewValue);
    }

    private void FadeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FadeValueText is not null)
        {
            FadeValueText.Text = $"{e.NewValue:0}%";
        }

        ApplyToSelection(s => s.FadeAmount = e.NewValue);
    }

    private void ShadowCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var enabled = ShadowCheckBox.IsChecked == true;
        ApplyToSelectionWithUndo(s => s.ShadowEnabled = enabled);
    }

    private void ShadowDistanceSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ShadowDistanceValueText is not null)
        {
            ShadowDistanceValueText.Text = $"{e.NewValue:0}px";
        }

        ApplyToSelection(s => s.ShadowDistance = e.NewValue);
    }

    private void ShadowStrengthSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ShadowStrengthValueText is not null)
        {
            ShadowStrengthValueText.Text = $"{e.NewValue:0}%";
        }

        ApplyToSelection(s => s.ShadowStrength = e.NewValue);
    }

    private void ShadowAngleSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ShadowAngleValueText is not null)
        {
            ShadowAngleValueText.Text = $"{e.NewValue:0}°";
        }

        ApplyToSelection(s => s.ShadowAngle = e.NewValue);
    }

    private void ShadowColorPicker_OnColorChanged(object? sender, Color color) =>
        ApplyToSelectionWithUndo(s => s.ShadowColor = color.FromMediaColor());

    private void TextBoldToggle_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var bold = TextBoldToggle.IsChecked == true;
        ApplyToSelectionWithUndo(s => s.TextBold = bold);
    }

    private void TextItalicToggle_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var italic = TextItalicToggle.IsChecked == true;
        ApplyToSelectionWithUndo(s => s.TextItalic = italic);
    }

    /// <summary>Collapses/expands the single sibling element right after a clicked card/section
    /// header — every collapsible header's XAML wraps its content into exactly one following
    /// Panel/UIElement so this one handler covers all of them.</summary>
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

    private void BackgroundModeRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_context is null || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        var mode = Enum.Parse<BackgroundMode>(tag);
        _context.Document.BackgroundMode = mode;
        BackgroundColorPicker.Visibility = mode == BackgroundMode.SolidColor ? Visibility.Visible : Visibility.Collapsed;
        _context.RequestRedraw();
    }

    private void BackgroundColorPicker_OnColorChanged(object? sender, Color color)
    {
        if (_context is null)
        {
            return;
        }

        _context.Document.BackgroundColor = color.FromMediaColor();
        _context.RequestRedraw();
    }

    private void RebuildLayerList()
    {
        _layerItems.Clear();

        if (_context is null)
        {
            return;
        }

        foreach (var layer in _context.Document.Layers)
        {
            _layerItems.Add(new LayerListItem(layer));
        }
    }

    private void AddLayerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        var layer = new Layer { Name = $"Layer {_context.Document.Layers.Count + 1}" };
        _context.Document.Layers.Insert(0, layer);
        _context.UndoManager.Push(UndoableAction.ForLayerInsert(_context.Document.Layers, layer, 0));
        _context.SelectedLayer = layer;
        LayerList.SelectedIndex = 0;
    }

    private void LayerList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_context is not null && LayerList.SelectedItem is LayerListItem item)
        {
            _context.SelectedLayer = item.Layer;
        }
    }

    /// <summary>The IsChecked TwoWay binding already flips Layer.Visible/Layer.Locked — this just
    /// forces a redraw, since hidden/locked shapes affect the canvas (and hit-testing) immediately.</summary>
    private void LayerToggle_OnClick(object sender, RoutedEventArgs e) => _context?.RequestRedraw();

    private void LayerForward_OnClick(object sender, RoutedEventArgs e) => MoveLayer(sender, -1);

    private void LayerBackward_OnClick(object sender, RoutedEventArgs e) => MoveLayer(sender, 1);

    private void MoveLayer(object sender, int delta)
    {
        if (_context is null || sender is not Button { Tag: Layer layer })
        {
            return;
        }

        var index = _context.Document.Layers.IndexOf(layer);
        if (index < 0)
        {
            return;
        }

        var newIndex = Math.Clamp(index + delta, 0, _context.Document.Layers.Count - 1);
        if (newIndex == index)
        {
            return;
        }

        _context.Document.Layers.Move(index, newIndex);
        _context.UndoManager.Push(UndoableAction.ForMove(_context.Document.Layers, index, newIndex));
        _context.RequestRedraw();
    }

    private void ShapeForward_OnClick(object sender, RoutedEventArgs e) => MoveShape(sender, -1);

    private void ShapeBackward_OnClick(object sender, RoutedEventArgs e) => MoveShape(sender, 1);

    /// <summary>Shape z-order control — brings a shape forward/backward within its own layer
    /// (index 0 = front-most, matching Layer.Shapes' existing convention).</summary>
    private void MoveShape(object sender, int delta)
    {
        if (_context is null || sender is not Button { Tag: ShapeListItem item })
        {
            return;
        }

        var shapes = item.Layer.Shapes;
        var index = shapes.IndexOf(item.Shape);
        if (index < 0)
        {
            return;
        }

        var newIndex = Math.Clamp(index + delta, 0, shapes.Count - 1);
        if (newIndex == index)
        {
            return;
        }

        shapes.Move(index, newIndex);
        _context.UndoManager.Push(UndoableAction.ForMove(shapes, index, newIndex));
        _context.RequestRedraw();
    }

    private void ShapeItem_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_context is null || sender is not Border { Tag: ShapeListItem item })
        {
            return;
        }

        _context.SelectedLayer = item.Layer;
        _context.SelectedShape = item.Shape;
        _context.RequestRedraw();
    }
}
