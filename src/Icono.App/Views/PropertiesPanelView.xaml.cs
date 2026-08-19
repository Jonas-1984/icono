using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;
using Icono.Core;

namespace Icono.App.Views;

public partial class PropertiesPanelView : UserControl
{
    private static readonly Color[] SwatchColors =
    [
        Colors.White, Colors.Black, Colors.Silver,
        Color.FromRgb(0x6F, 0xB1, 0xFF), Color.FromRgb(0x2E, 0x86, 0xFF), Color.FromRgb(0x1B, 0x4F, 0x91),
        Color.FromRgb(0xE5, 0x39, 0x35), Color.FromRgb(0xFB, 0x8C, 0x00), Color.FromRgb(0xFD, 0xD8, 0x35),
        Color.FromRgb(0x43, 0xA0, 0x47), Color.FromRgb(0x8E, 0x24, 0xAA), Color.FromRgb(0x00, 0xAC, 0xC1),
    ];

    private readonly ObservableCollection<LayerListItem> _layerItems = new();
    private EditorContext? _context;
    private Shape? _boundShape;

    public EditorContext? Context
    {
        get => _context;
        set
        {
            if (_context is not null)
            {
                _context.PropertyChanged -= OnContextPropertyChanged;
            }

            _context = value;

            if (_context is not null)
            {
                _context.PropertyChanged += OnContextPropertyChanged;
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

        BorderPresetList.ItemsSource = new List<BorderStylePreset>
        {
            new("Blue Gloss", (Brush)FindResource("GlossyBlueBrush")),
            new("Silver Gloss", new LinearGradientBrush(Colors.White, Color.FromRgb(0x8A, 0x8A, 0x8A), 90)),
            new("Gold Gloss", new LinearGradientBrush(Color.FromRgb(0xFF, 0xE6, 0x8A), Color.FromRgb(0xB8, 0x86, 0x0B), 90)),
            new("Red Gloss", new LinearGradientBrush(Color.FromRgb(0xFF, 0x8A, 0x80), Color.FromRgb(0x8E, 0x1B, 0x1B), 90)),
        };

        LayerList.ItemsSource = _layerItems;
        FillColorPicker.ColorChanged += FillColorPicker_OnColorChanged;
        BackgroundColorPicker.ColorChanged += BackgroundColorPicker_OnColorChanged;
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorContext.SelectedShape))
        {
            RefreshForSelection();
        }
    }

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

        BorderThicknessSlider.Value = shape.BorderThickness;
        CornerRadiusSlider.Value = shape.CornerRadius;
        OpacitySlider.Value = shape.Opacity;
        BlurSlider.Value = shape.BlurRadius;
        FadeSlider.Value = shape.FadeAmount;
        ShadowCheckBox.IsChecked = shape.ShadowEnabled;
        ShadowDistanceSlider.Value = shape.ShadowDistance;
        ShadowStrengthSlider.Value = shape.ShadowStrength;

        FillColorPicker.SelectedColor = shape.FillColor.ToMediaColor();
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

    private void FillColorPicker_OnColorChanged(object? sender, Color color)
    {
        if (_context is null)
        {
            return;
        }

        _context.CurrentFillColor = color.FromMediaColor();
        ApplyToSelection(s => s.FillColor = color.FromMediaColor());
    }

    private void FillSwatch_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_context is null || sender is not Border { Background: SolidColorBrush brush })
        {
            return;
        }

        _context.CurrentFillColor = brush.Color.FromMediaColor();
        ApplyToSelection(s => s.FillColor = brush.Color.FromMediaColor());
    }

    private void BorderPreset_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: BorderStylePreset preset })
        {
            return;
        }

        var color = preset.Preview switch
        {
            SolidColorBrush solid => solid.Color,
            LinearGradientBrush { GradientStops.Count: > 0 } gradient => gradient.GradientStops[0].Color,
            _ => Colors.Gray,
        };

        ApplyToSelection(s => s.BorderColor = color.FromMediaColor());
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

        switch (field)
        {
            case "X": _boundShape.X = value; break;
            case "Y": _boundShape.Y = value; break;
            case "Width": _boundShape.Width = Math.Max(1, value); break;
            case "Height": _boundShape.Height = Math.Max(1, value); break;
        }

        _context?.RequestRedraw();
    }

    private void BorderThicknessSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BorderThicknessValueText is not null)
        {
            BorderThicknessValueText.Text = $"{e.NewValue:0}px";
        }

        ApplyToSelection(s => s.BorderThickness = e.NewValue);
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
        ApplyToSelection(s => s.ShadowEnabled = enabled);
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
        _layerItems.Insert(0, new LayerListItem(layer));
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
        _layerItems.Move(index, newIndex);
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
