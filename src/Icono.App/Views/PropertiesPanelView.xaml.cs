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
using Icono.Rendering;
using SkiaSharp;
using System.IO;
using System.Windows.Media.Imaging;

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

    // Order matches the reference "line ends" picker: plain line first, then wing-shaped heads
    // (open/filled/thin/half), then the dot-style markers (circle/diamond/square/bar).
    internal static readonly ArrowHeadOption[] ArrowHeadOptions =
    [
        new("None", ArrowHeadKind.None),
        new("Open Arrow", ArrowHeadKind.Open),
        new("Filled Arrow", ArrowHeadKind.Filled),
        new("Thin Arrow", ArrowHeadKind.Thin),
        new("Half Arrow", ArrowHeadKind.Half),
        new("Circle", ArrowHeadKind.Circle),
        new("Diamond", ArrowHeadKind.Diamond),
        new("Square", ArrowHeadKind.Square),
        new("Bar", ArrowHeadKind.Bar),
    ];

    private static readonly FadeDirectionOption[] FadeDirectionOptions =
    [
        new("Top → Bottom", FadeDirection.TopToBottom),
        new("Bottom → Top", FadeDirection.BottomToTop),
        new("Left → Right", FadeDirection.LeftToRight),
        new("Right → Left", FadeDirection.RightToLeft),
        new("Both Sides (Vertical)", FadeDirection.VerticalBothSides),
        new("Both Sides (Horizontal)", FadeDirection.HorizontalBothSides),
    ];

    private readonly ObservableCollection<LayerListItem> _layerItems = new();
    private EditorContext? _context;
    private Shape? _boundShape;
    private List<ShapeSnapshot>? _sliderUndoBefore;

    /// <summary>True while <see cref="RefreshControlsFromShape"/> is programmatically setting
    /// control values to DISPLAY the bound shape's state. Every control's change-event handler
    /// funnels its actual shape mutation through <see cref="ApplyToSelection"/>/
    /// <see cref="ApplyToSelectionWithUndo"/>, which both check this flag first — without it, a
    /// control whose displayed value happens to differ from what a DIFFERENT selected shape has
    /// (e.g. selecting a 50%-opacity shape together with a 100%-opacity one) fires its change
    /// event purely from being re-displayed, silently overwriting every selected shape's property
    /// to match whichever shape the panel happens to be showing.</summary>
    private bool _isRefreshingControls;
    private Layer? _accentColorTargetLayer;

    public EditorContext? Context
    {
        get => _context;
        set
        {
            if (_context is not null)
            {
                _context.PropertyChanged -= OnContextPropertyChanged;
                _context.ScaleChanged -= OnScaleChanged;
                _context.SheetChanged -= RefreshDocumentSettings;
                _context.Document.Layers.CollectionChanged -= OnDocumentLayersChanged;
            }

            _context = value;

            if (_context is not null)
            {
                _context.PropertyChanged += OnContextPropertyChanged;
                _context.ScaleChanged += OnScaleChanged;
                _context.SheetChanged += RefreshDocumentSettings;
                _context.Document.Layers.CollectionChanged += OnDocumentLayersChanged;
                RefreshDocumentSettings();
            }

            RebuildLayerList();
            RefreshForSelection();
        }
    }

    // ----- API used by the main window's menus ----------------------------------------------------

    private bool _syncingDocumentSettings;

    /// <summary>Re-reads the page settings (background mode and values, canvas size) from the document into the controls —
    /// used when a document was created or opened.</summary>
    public void RefreshDocumentSettings()
    {
        if (_context is null)
        {
            return;
        }

        _syncingDocumentSettings = true;
        try
        {
            SyncScaleControls();
            SyncSheetControls();
            var document = _context.Document;
            BgTransparentRadio.IsChecked = document.BackgroundMode == BackgroundMode.Transparent;
            BgSolidRadio.IsChecked = document.BackgroundMode == BackgroundMode.SolidColor;
            BgBlurRadio.IsChecked = document.BackgroundMode == BackgroundMode.Blur;
            BgImageRadio.IsChecked = document.BackgroundMode == BackgroundMode.Image;
            BackgroundColorPicker.SelectedColor = document.BackgroundColor.ToMediaColor();
            UpdateBackgroundPanels();
            BackgroundBlurColorPicker.SelectedColor = document.BackgroundBlurColor.ToMediaColor();
            BackgroundBlurGradientCheckBox.IsChecked = document.BackgroundBlurGradientEnabled;
            BackgroundBlurGradientColorPicker.Visibility =
                document.BackgroundBlurGradientEnabled ? Visibility.Visible : Visibility.Collapsed;
            BackgroundBlurGradientColorPicker.SelectedColor = document.BackgroundBlurGradientColor.ToMediaColor();
            BackgroundBlurAmountSlider.Value = document.BackgroundBlurAmount;
            BackgroundFadeAmountSlider.Value = document.BackgroundFadeAmount;
            BackgroundOpacitySlider.Value = document.BackgroundOpacity;
            RefreshCanvasSizeTextBoxes();
        }
        finally
        {
            _syncingDocumentSettings = false;
        }
    }

    /// <summary>Adds a new layer on top and selects it (also reachable from the Insert menu).</summary>
    public void AddLayer() => AddLayerButton_OnClick(this, new RoutedEventArgs());

    /// <summary>Applies a design style (with its border / shadow preset) to the selected shapes. False when nothing styleable is selected.</summary>
    public bool ApplyDesignStyle(DesignStyleKind kind)
    {
        if (_context is null || !_context.SelectedShapes.Any(s => !s.Locked && DesignStyleRenderer.Supports(s)))
        {
            return false;
        }

        ApplyToSelectionWithUndo(s =>
        {
            if (DesignStyleRenderer.Supports(s))
            {
                DesignStylePresets.Apply(s, kind);
            }
        });
        return true;
    }

    /// <summary>Scrolls to the card whose header reads <paramref name="headerText"/>, expands it and makes it glow briefly. False
    /// when that card is not available right now (e.g. the LINE card needs a selected line).</summary>
    public bool FocusCard(string headerText)
    {
        var header = FindCardHeader(this, headerText);
        if (header is null)
        {
            return false;
        }

        // Cards that are hidden for the current selection cannot be focused.
        for (DependencyObject? d = header; d is not null && !ReferenceEquals(d, this); d = VisualTreeHelper.GetParent(d))
        {
            if (d is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        if (header.Parent is Panel parent)
        {
            var index = parent.Children.IndexOf(header);
            if (index >= 0 && index + 1 < parent.Children.Count && parent.Children[index + 1] is UIElement content && content.Visibility != Visibility.Visible)
            {
                content.Visibility = Visibility.Visible;
                if (header is Grid { Children.Count: > 1 } grid && grid.Children[1] is System.Windows.Shapes.Path chevron)
                {
                    chevron.RenderTransform = new RotateTransform(0);
                }
            }
        }

        // Scroll so the card's header sits at the top of the panel (the card then shows as much of itself as fits).
        ScrollViewer? scroller = null;
        for (DependencyObject? d = header; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ScrollViewer sv)
            {
                scroller = sv;
                break;
            }
        }

        if (scroller is not null)
        {
            var top = header.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y;
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset + top - 8);
        }
        else
        {
            header.BringIntoView();
        }

        Border? card = null;
        for (DependencyObject? d = header; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is Border b && b.Style is not null && ReferenceEquals(b.Style, TryFindResource("CardBorderStyle")))
            {
                card = b;
                break;
            }
        }

        if (card is not null)
        {
            // A glow Effect on the card would run all its text through a blurred offscreen bitmap; light the rim instead.
            card.SetResourceReference(Border.BorderBrushProperty, "AccentBlueMidBrush");
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                card.ClearValue(Border.BorderBrushProperty);
            };
            timer.Start();
        }

        return true;
    }

    /// <summary>The collapsible header row (a Grid inside a card) containing a TextBlock with exactly this text.</summary>
    private static Grid? FindCardHeader(DependencyObject root, string headerText)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text && string.Equals(text.Text, headerText, StringComparison.OrdinalIgnoreCase) &&
                text.Parent is Grid { Parent: StackPanel } header)
            {
                return header;
            }

            if (FindCardHeader(child, headerText) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public PropertiesPanelView()
    {
        InitializeComponent();
        InitializeDesignStylePicker();

        SwatchList.ItemsSource = SwatchColors.Select(c => (Brush)new SolidColorBrush(c)).ToList();

        var presets = new List<BorderStylePreset>
        {
            new("Blue Gloss", (Brush)FindResource("BlueGlossPresetBrush")),
            new("Silver Gloss", new LinearGradientBrush(Colors.White, Color.FromRgb(0x8A, 0x8A, 0x8A), 90)),
            new("Gold Gloss", new LinearGradientBrush(Color.FromRgb(0xFF, 0xE6, 0x8A), Color.FromRgb(0xB8, 0x86, 0x0B), 90)),
            new("Red Gloss", new LinearGradientBrush(Color.FromRgb(0xFF, 0x8A, 0x80), Color.FromRgb(0x8E, 0x1B, 0x1B), 90)),
            new("Acrylic", new LinearGradientBrush(Color.FromArgb(210, 255, 255, 255), Color.FromArgb(60, 255, 255, 255), 90),
                ApplyAcrylicPreset),
        };
        presets.AddRange(PresetColors.Select(pc => new BorderStylePreset(pc.Name, new SolidColorBrush(pc.Color))));
        BorderPresetList.ItemsSource = presets;

        LayerList.ItemsSource = _layerItems;
        LineStartHeadComboBox.ItemsSource = ArrowHeadOptions;
        LineEndHeadComboBox.ItemsSource = ArrowHeadOptions;
        FadeDirectionComboBox.ItemsSource = FadeDirectionOptions;
        WallHatchComboBox.ItemsSource = HatchOptions;
        WallUnitComboBox.ItemsSource = WallUnitOptions;
        DimensionUnitComboBox.ItemsSource = WallUnitOptions;
        DimensionArrowStyleComboBox.ItemsSource = ArrowStyleOptions;
        WallHatchColorPicker.ColorChanged += WallHatchColorPicker_OnColorChanged;

        // Every font Windows currently has installed — lets the user pick any of their own fonts
        // instead of the app hard-coding a fixed "Segoe UI".
        TextFontFamilyComboBox.ItemsSource = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new FontFamilyOption(name))
            .ToList();
        LayerAccentColorSwatchList.ItemsSource = PresetColors.Select(pc => (Brush)new SolidColorBrush(pc.Color)).ToList();
        FillColorPicker.ColorChanged += FillColorPicker_OnColorChanged;
        BackgroundColorPicker.ColorChanged += BackgroundColorPicker_OnColorChanged;
        BackgroundBlurColorPicker.ColorChanged += BackgroundBlurColorPicker_OnColorChanged;
        BackgroundBlurGradientColorPicker.ColorChanged += BackgroundBlurGradientColorPicker_OnColorChanged;
        BorderColorPicker.ColorChanged += BorderColorPicker_OnColorChanged;
        BorderGradientColorPicker.ColorChanged += BorderGradientColorPicker_OnColorChanged;
        ShadowColorPicker.ColorChanged += ShadowColorPicker_OnColorChanged;
        TextColorPicker.ColorChanged += TextColorPicker_OnColorChanged;
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

        if (_boundShape.Locked)
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
        if (shape is null)
        {
            DimensionCard.Visibility = Visibility.Collapsed;
            WallCard.Visibility = Visibility.Collapsed;
            LineCard.Visibility = Visibility.Collapsed;
            DesignStyleCard.Visibility = Visibility.Collapsed;
            DesignGridCard.Visibility = Visibility.Collapsed;
        }

        RefreshControlsFromShape();
    }

    private bool _shapeRefreshQueued;

    /// <summary>A drag changes several properties of the shape per mouse move (X, Y, points, size ...); the panel is brought up to date
    /// once per frame instead of once per property.</summary>
    private void OnBoundShapePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_shapeRefreshQueued)
        {
            return;
        }

        _shapeRefreshQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            _shapeRefreshQueued = false;
            if (_boundShape is null)
            {
                return;
            }

            RefreshControlsFromShape();
            RefreshForSelection();
        });
    }

    private void RefreshControlsFromShape()
    {
        var shape = _boundShape;
        if (shape is null)
        {
            return;
        }

        _isRefreshingControls = true;
        try
        {
            RefreshControlsFromShapeCore(shape);
        }
        finally
        {
            _isRefreshingControls = false;
        }
    }

    private void RefreshControlsFromShapeCore(Shape shape)
    {
        var drawn = ShapeOutline.Box(shape);
        XTextBox.Text = FormatReal(drawn.X);
        YTextBox.Text = FormatReal(drawn.Y);
        WidthTextBox.Text = FormatReal(drawn.W);
        HeightTextBox.Text = FormatReal(drawn.H);
        UpdateScaleNote();
        RotationTextBox.Text = shape.Rotation.ToString("0", CultureInfo.InvariantCulture);

        RefreshLineControls(shape);
        RefreshWallControls(shape);
        RefreshDimensionControls(shape);
        RefreshDesignStyleControls(shape);
        RefreshDesignGridControls(shape);
        Depth3DSlider.Value = shape.Depth3D;
        LineStartHeadComboBox.SelectedItem = ArrowHeadOptions.First(o => o.Value == shape.LineStartHead);
        LineEndHeadComboBox.SelectedItem = ArrowHeadOptions.First(o => o.Value == shape.LineEndHead);
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
        FadeDirectionComboBox.SelectedItem = FadeDirectionOptions.First(o => o.Value == shape.FadeDirection);
        ShadowCheckBox.IsChecked = shape.ShadowEnabled;
        ShadowDistanceSlider.Value = shape.ShadowDistance;
        ShadowStrengthSlider.Value = shape.ShadowStrength;
        ShadowBlurSlider.Value = shape.ShadowBlurRadius;
        ShadowAngleSlider.Value = shape.ShadowAngle;
        ShadowColorPicker.SelectedColor = shape.ShadowColor.ToMediaColor();

        FillColorPicker.SelectedColor = shape.FillColor.ToMediaColor();
        FillEnabledCheckBox.IsChecked = shape.FillEnabled;
        FillColorControls.Visibility = shape.FillEnabled ? Visibility.Visible : Visibility.Collapsed;

        var fontFamily = string.IsNullOrWhiteSpace(shape.TextFontFamily) ? "Segoe UI" : shape.TextFontFamily;
        TextFontFamilyComboBox.SelectedItem = (TextFontFamilyComboBox.ItemsSource as IEnumerable<FontFamilyOption>)
            ?.FirstOrDefault(o => o.Label == fontFamily);
        TextBoldToggle.IsChecked = shape.TextBold;
        TextItalicToggle.IsChecked = shape.TextItalic;
        TextUnderlineToggle.IsChecked = shape.TextUnderline;
        FontSizeTextBox.Text = shape.TextFontSize.ToString("0", CultureInfo.InvariantCulture);

        // A Text shape is coloured by its fill; every other shape gets a separate colour for the text typed into it.
        TextColorSection.Visibility = shape.Kind == ShapeKind.Text ? Visibility.Collapsed : Visibility.Visible;
        TextColorPicker.SelectedColor = ShapeLabel.EffectiveColor(shape).ToMediaColor();
        TextColorAutoButton.IsEnabled = shape.TextColor is not null;
    }

    /// <summary>Applies an edit to every currently selected, UNLOCKED shape (or a no-op if none) —
    /// a locked shape must not change via any Properties-panel control (color, opacity, blur,
    /// shadow, border, corner radius, line ends, text formatting, ...), same as it already can't
    /// be moved/resized/rotated on the canvas.</summary>
    private void ApplyToSelection(Action<Shape> apply)
    {
        if (_context is null || _isRefreshingControls)
        {
            return;
        }

        foreach (var shape in _context.SelectedShapes.Where(s => !s.Locked))
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
        if (_context is null || _context.SelectedShapes.Count == 0 || _isRefreshingControls)
        {
            return;
        }

        var shapes = _context.SelectedShapes.Where(s => !s.Locked).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

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
        if (_boundShape is null || _boundShape.Locked || sender is not TextBox { Tag: string field } textBox)
        {
            return;
        }

        // The boxes show the size of what is DRAWN (its outline), not of the template frame around it.
        var outline = ShapeOutline.Box(_boundShape);
        var shownPx = field switch
        {
            "X" => outline.X,
            "Y" => outline.Y,
            "Width" => outline.W,
            _ => outline.H,
        };

        // Untouched box (focus just left it): keep the exact stored value instead of the rounded text.
        var text = textBox.Text.Trim().Replace(',', '.');
        if (text == FormatReal(shownPx))
        {
            return;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
        {
            RefreshControlsFromShape();
            return;
        }

        var value = Math.Max(0, RealToPx(typed));

        var before = ShapeSnapshot.Capture(_boundShape);

        switch (field)
        {
            case "X": ShapeOutline.SetBox(_boundShape, value, outline.Y, outline.W, outline.H); break;
            case "Y": ShapeOutline.SetBox(_boundShape, outline.X, value, outline.W, outline.H); break;
            case "Width": ShapeOutline.SetBox(_boundShape, outline.X, outline.Y, Math.Max(1, value), outline.H); break;
            case "Height": ShapeOutline.SetBox(_boundShape, outline.X, outline.Y, outline.W, Math.Max(1, value)); break;
        }

        var after = ShapeSnapshot.Capture(_boundShape);
        if (_context is not null && !after.Equals(before))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([_boundShape], [before], [after]));
        }

        _context?.RequestRedraw();
    }

    private void RotateUpButton_OnClick(object sender, RoutedEventArgs e) => ApplyRotationDelta(1);

    private void RotateDownButton_OnClick(object sender, RoutedEventArgs e) => ApplyRotationDelta(-1);

    private void ApplyRotationDelta(double delta)
    {
        if (_boundShape is null || _boundShape.Locked)
        {
            return;
        }

        var before = ShapeSnapshot.Capture(_boundShape);
        _boundShape.Rotation += delta;
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
        if (_boundShape is null || _boundShape.Locked)
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
        ApplyToSelectionWithUndo(s =>
        {
            s.PathRoutingStyle = style;

            // The Rounded connector needs a visible radius, otherwise the corner controls would read "sharp"
            // while the line is drawn with a built-in fallback radius.
            if (style == PathRoutingStyle.Rounded && s.CornerRadius <= 0 && IsLineFamily(s))
            {
                s.CornerRadius = 8;
            }
        });
    }

    private void TextFontFamilyComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TextFontFamilyComboBox.SelectedItem is not FontFamilyOption option)
        {
            return;
        }

        ApplyToSelectionWithUndo(s => s.TextFontFamily = option.Label);
    }

    private void FadeDirectionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FadeDirectionComboBox.SelectedItem is not FadeDirectionOption option)
        {
            return;
        }

        ApplyToSelectionWithUndo(s => s.FadeDirection = option.Value);
    }

    private void LineStartHeadComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineStartHeadComboBox.SelectedItem is not ArrowHeadOption option)
        {
            return;
        }

        ApplyToSelectionWithUndo(s => s.LineStartHead = option.Value);
    }

    private void LineEndHeadComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineEndHeadComboBox.SelectedItem is not ArrowHeadOption option)
        {
            return;
        }

        ApplyToSelectionWithUndo(s => s.LineEndHead = option.Value);
    }

    // ----- Design style picker (Glassmorphism, Neumorphism, ...) ------------------------------------

    private readonly List<DesignStyleOption> _designStyleOptions =
    [
        new() { Kind = DesignStyleKind.None, Label = "None (standard)", ToolTip = "Plain fill and border" },
        new() { Kind = DesignStyleKind.Glassmorphism, Label = "Glassmorphism", ToolTip = "Glassmorphism — frosted translucent glass with a bright edge" },
        new() { Kind = DesignStyleKind.Neumorphism, Label = "Neumorphism", ToolTip = "Neumorphism — soft extruded surface with light and dark shadows" },
        new() { Kind = DesignStyleKind.FluentDesign, Label = "Fluent Design", ToolTip = "Fluent Design — acrylic surface with layered elevation and reveal light" },
        new() { Kind = DesignStyleKind.AuroraGradients, Label = "Aurora Gradients", ToolTip = "Aurora Gradients — flowing multi-hue gradient with soft light blobs" },
        new() { Kind = DesignStyleKind.BentoUI, Label = "Bento UI", ToolTip = "Bento UI — calm rounded tile with hairline border and soft shadow" },
        new() { Kind = DesignStyleKind.Cyberpunk, Label = "Cyberpunk / Futuristic UI", ToolTip = "Cyberpunk / Futuristic UI — dark panel, scanlines, cyan neon and magenta offset edge" },
        new() { Kind = DesignStyleKind.Claymorphism3D, Label = "3D Claymorphism", ToolTip = "3D Claymorphism — inflated puffy clay with inner light and shadow" },
        new() { Kind = DesignStyleKind.DarkNeon, Label = "Dark UI + Neon Accents", ToolTip = "Dark UI + Neon Accents — dark card with a glowing neon edge" },
    ];

    private (ArgbColor Fill, ArgbColor Border)? _designStylePreviewColors;

    private void InitializeDesignStylePicker() => DesignStyleList.ItemsSource = _designStyleOptions;

    private void RefreshDesignStyleControls(Shape shape)
    {
        DesignStyleCard.Visibility = DesignStyleRenderer.Supports(shape) ? Visibility.Visible : Visibility.Collapsed;
        if (DesignStyleCard.Visibility != Visibility.Visible)
        {
            return;
        }

        // The thumbnails show each style in the selected shape's own colour; re-render only when that colour changes.
        var colors = (shape.FillColor, shape.BorderColor);
        if (_designStylePreviewColors != colors)
        {
            _designStylePreviewColors = colors;
            foreach (var option in _designStyleOptions)
            {
                option.Thumbnail = RenderDesignStylePreview(option.Kind, shape.FillColor, shape.BorderColor);
            }
        }

        foreach (var option in _designStyleOptions)
        {
            option.IsSelected = option.Kind == shape.DesignStyle;
        }
    }

    private void DesignStyle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DesignStyleOption option })
        {
            return;
        }

        // Choosing a style also fills BORDER / EFFECTS with values that suit it; the user edits them from there.
        ApplyToSelectionWithUndo(s => DesignStylePresets.Apply(s, option.Kind));
        e.Handled = true;
    }

    /// <summary>A small rounded-square sample of one design style, rendered with the real shape renderer.</summary>
    private static ImageSource RenderDesignStylePreview(DesignStyleKind kind, ArgbColor fill, ArgbColor border)
    {
        const int size = 96;
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var sample = new Shape
        {
            Kind = ShapeKind.RoundedRectangle,
            X = 13,
            Y = 13,
            Width = size - 26,
            Height = size - 26,
            CornerRadius = 20,
            FillColor = fill,
            BorderColor = border,
            BorderThickness = 3,
            DesignStyle = kind,
        };
        DesignStylePresets.Apply(sample, kind);
        ShapeRenderer.Draw(canvas, sample);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());

        var result = new BitmapImage();
        result.BeginInit();
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();
        return result;
    }

    // ----- Design grid options ------------------------------------------------------------------

    private void RefreshDesignGridControls(Shape shape)
    {
        var isGrid = shape.Kind == ShapeKind.DesignGrid;
        DesignGridCard.Visibility = isGrid ? Visibility.Visible : Visibility.Collapsed;
        if (!isGrid)
        {
            return;
        }

        DesignGridCellSizeTextBox.Text = (shape.GridCellSize / DesignGridMath.PxPerMm).ToString("0.##", CultureInfo.InvariantCulture);
        DesignGridColumnsTextBox.Text = DesignGridMath.Columns(shape).ToString(CultureInfo.InvariantCulture);
        DesignGridRowsTextBox.Text = DesignGridMath.Rows(shape).ToString(CultureInfo.InvariantCulture);
        DesignGridLinesCheckBox.IsChecked = shape.GridShowLines;
    }

    private void DesignGridCellSizeTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_boundShape is null || _isRefreshingControls)
        {
            return;
        }

        if (!double.TryParse(DesignGridCellSizeTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm))
        {
            RefreshControlsFromShape();
            return;
        }

        var px = Math.Clamp(mm, 0.5, 50) * DesignGridMath.PxPerMm;

        // Keep the number of squares and rescale the grid around them.
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind != ShapeKind.DesignGrid)
            {
                return;
            }

            var cols = DesignGridMath.Columns(s);
            var rows = DesignGridMath.Rows(s);
            s.GridCellSize = px;
            s.Width = cols * px;
            s.Height = rows * px;
        });
        RefreshControlsFromShape();
    }

    private void DesignGridColumnsTextBox_OnLostFocus(object sender, RoutedEventArgs e) =>
        ApplyDesignGridCount(DesignGridColumnsTextBox, (s, count) => s.Width = count * s.GridCellSize);

    private void DesignGridRowsTextBox_OnLostFocus(object sender, RoutedEventArgs e) =>
        ApplyDesignGridCount(DesignGridRowsTextBox, (s, count) => s.Height = count * s.GridCellSize);

    private void ApplyDesignGridCount(TextBox textBox, Action<Shape, int> apply)
    {
        if (_boundShape is null || _isRefreshingControls)
        {
            return;
        }

        if (!int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            RefreshControlsFromShape();
            return;
        }

        count = Math.Clamp(count, 1, 2000);
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind == ShapeKind.DesignGrid)
            {
                apply(s, count);
            }
        });
        RefreshControlsFromShape();
    }

    private void DesignGridLinesCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var show = DesignGridLinesCheckBox.IsChecked == true;
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind == ShapeKind.DesignGrid)
            {
                s.GridShowLines = show;
            }
        });
    }

    private void DesignGridClearButton_OnClick(object sender, RoutedEventArgs e) =>
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind == ShapeKind.DesignGrid)
            {
                s.GridCells = null;
            }
        });

    // ----- Wall (thickness, hatch, ring, dimensions, splitting) ----------------------------------------------

    private static readonly HatchOption[] HatchOptions =
    [
        new("Solid (no hatch)", HatchKind.Solid),
        new("Diagonal", HatchKind.Diagonal),
        new("Reverse diagonal", HatchKind.ReverseDiagonal),
        new("Cross hatch", HatchKind.CrossHatch),
        new("Horizontal lines", HatchKind.Horizontal),
        new("Vertical lines", HatchKind.Vertical),
        new("Grid", HatchKind.Grid),
        new("Dots", HatchKind.Dots),
        new("Brick", HatchKind.Brick),
        new("Zigzag", HatchKind.Zigzag),
    ];

    private static readonly LengthUnitOption[] WallUnitOptions =
    [
        new("Millimetres (mm)", LengthUnit.Millimeter),
        new("Centimetres (cm)", LengthUnit.Centimeter),
        new("Metres (m)", LengthUnit.Meter),
        new("Inches (in)", LengthUnit.Inch),
    ];

    private static string UnitName(CanvasSizeUnit unit) => unit switch
    {
        CanvasSizeUnit.Mm => "mm",
        CanvasSizeUnit.Cm => "cm",
        CanvasSizeUnit.In => "inch",
        _ => "px",
    };

    private void RefreshWallControls(Shape shape)
    {
        var isWall = shape.Kind == ShapeKind.Wall;
        WallCard.Visibility = isWall ? Visibility.Visible : Visibility.Collapsed;
        if (!isWall)
        {
            return;
        }

        WallThicknessLabel.Text = $"THICKNESS ({UnitName(_shapeSizeUnit)})";
        WallThicknessTextBox.Text = FormatReal(shape.WallThickness);
        WallClosedCheckBox.IsChecked = shape.WallClosed;
        WallHatchComboBox.SelectedItem = HatchOptions.First(o => o.Value == shape.Hatch);
        WallHatchSpacingSlider.Value = Math.Clamp(shape.HatchSpacing, WallHatchSpacingSlider.Minimum, WallHatchSpacingSlider.Maximum);
        WallHatchSpacingValueText.Text = $"{shape.HatchSpacing:0}px";
        WallHatchColorPicker.SelectedColor = shape.HatchColor.ToMediaColor();
        WallDimensionsCheckBox.IsChecked = shape.WallShowDimensions;
        WallUnitComboBox.SelectedItem = WallUnitOptions.First(o => o.Value == shape.WallDimensionUnit);
    }

    // ----- Dimension card (a dimension shape, and the dimensions written next to a wall) ------------------------

    private static bool HasDimensions(Shape shape) =>
        shape.Kind == ShapeKind.Dimension || (shape.Kind == ShapeKind.Wall && shape.WallShowDimensions);

    private void RefreshDimensionControls(Shape shape)
    {
        var isDimension = shape.Kind == ShapeKind.Dimension;
        var isWall = shape.Kind == ShapeKind.Wall;
        var visible = HasDimensions(shape);
        DimensionCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }


        var linked = isDimension && shape.DimTarget is not null && shape.DimRole != DimensionRole.None;
        DimensionLinkSection.Visibility = isDimension ? Visibility.Visible : Visibility.Collapsed;
        DimensionLinkText.Text = linked
            ? "Attached to its shape: it moves and changes with it."
            : shape.DimAnchors is { Count: > 0 } && shape.DimTarget is null && shape.DimAnchors.Any(a => a is not null)
                ? "Stuck to its shape by its points: it follows when the shape moves or is resized."
                : "Free dimension (its points are its own).";
        DimensionDetachButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
        DimensionUnitSection.Visibility = isDimension ? Visibility.Visible : Visibility.Collapsed;
        DimensionColorHint.Visibility = isDimension ? Visibility.Visible : Visibility.Collapsed;

        // A wall has one dimension per segment: pick which one the fields below edit (or the wall's own settings for all of them).
        if (!ReferenceEquals(_dimensionSegmentShape, shape))
        {
            _dimensionSegmentShape = shape;
            _dimensionSegment = -1;
        }

        var segment = isWall ? _dimensionSegment : -1;
        DimensionSegmentSection.Visibility = isWall ? Visibility.Visible : Visibility.Collapsed;
        if (isWall)
        {
            var options = new List<SegmentOption> { new("All dimensions (wall settings)", -1) };
            var lengths = WallGeometry.Segments(shape);
            for (var i = 0; i < lengths.Count; i++)
            {
                var dx = lengths[i].To.X - lengths[i].From.X;
                var dy = lengths[i].To.Y - lengths[i].From.Y;
                var hidden = WallRenderer.FindOverride(shape, i) is { Hidden: true };
                var measured = DimensionRenderer.Format(Math.Sqrt(dx * dx + dy * dy), shape.WallDimensionUnit, shape.DimPrecision);
                options.Add(new SegmentOption($"{i + 1}:  {measured}{(hidden ? "  (hidden)" : string.Empty)}", i));
            }

            if (segment >= lengths.Count)
            {
                segment = _dimensionSegment = -1;
            }

            // the list is rebuilt only when its entries changed (every drag frame refreshes this card)
            var listKey = string.Join("|", options.Select(o => o.Label));
            if (listKey != _segmentListKey)
            {
                _segmentListKey = listKey;
                DimensionSegmentComboBox.ItemsSource = options;
            }

            DimensionSegmentComboBox.SelectedItem = ((IEnumerable<SegmentOption>)DimensionSegmentComboBox.ItemsSource).First(o => o.Index == segment);
        }

        var custom = isWall && segment >= 0 ? WallRenderer.FindOverride(shape, segment) : null;
        DimensionSegmentOptions.Visibility = isWall && segment >= 0 ? Visibility.Visible : Visibility.Collapsed;
        DimensionShowCheckBox.IsChecked = custom is not { Hidden: true };
        DimensionFlipCheckBox.IsChecked = custom is { Flip: true };
        if (_context is not null)
        {
            _context.WallSegmentHighlight = isWall && segment >= 0 ? segment : null;
        }

        // The distance is editable for a wall, and for a dimension that was created for its shape.
        DimensionOffsetSection.Visibility = isWall || linked ? Visibility.Visible : Visibility.Collapsed;
        DimensionOffsetLabel.Text = isWall
            ? $"DISTANCE FROM WALL ({UnitName(_shapeSizeUnit)})"
            : $"DISTANCE FROM SHAPE ({UnitName(_shapeSizeUnit)})";
        DimensionOffsetTextBox.Text = FormatInUnit(custom?.Offset ?? shape.DimOffset, _shapeSizeUnit);

        DimensionTextSection.Visibility = isDimension || (isWall && segment >= 0) ? Visibility.Visible : Visibility.Collapsed;
        DimensionTextBox.Text = isDimension ? shape.DimText ?? string.Empty : custom?.Text ?? string.Empty;
        DimensionTextPositionTextBox.Text = ((custom?.TextPosition ?? shape.DimTextPosition ?? 0.5) * 100).ToString("0.##", CultureInfo.InvariantCulture);
        DimensionTextShiftTextBox.Text = (custom?.TextShift ?? shape.DimTextShift).ToString("0.##", CultureInfo.InvariantCulture);

        if (isDimension)
        {
            DimensionUnitComboBox.SelectedItem = WallUnitOptions.First(o => o.Value == shape.DimUnit);
        }

        DimensionPrecisionTextBox.Text = shape.DimPrecision.ToString(CultureInfo.InvariantCulture);

        DimensionArrowStyleComboBox.SelectedItem = ArrowStyleOptions.First(o => o.Value == shape.DimArrowStyle);

        var style = DimensionStyle.For(shape);
        DimensionFontSizeTextBox.Text = style.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
        DimensionLineWidthTextBox.Text = style.LineWidth.ToString("0.##", CultureInfo.InvariantCulture);
        DimensionArrowSizeTextBox.Text = style.ArrowSize.ToString("0.##", CultureInfo.InvariantCulture);
        DimensionExtGapTextBox.Text = style.ExtGap.ToString("0.##", CultureInfo.InvariantCulture);
        DimensionExtOvershootTextBox.Text = style.ExtOvershoot.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void DimensionDetachButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind == ShapeKind.Dimension)
            {
                s.DimTarget = null;
                s.DimRole = DimensionRole.None;
                s.DimAnchors = null;
            }
        });
        RefreshControlsFromShape();
    }

    private void DimensionUnitComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DimensionUnitComboBox.SelectedItem is LengthUnitOption option)
        {
            if (_context is not null && !_isRefreshingControls)
            {
                _context.DimensionUnit = option.Value; // new dimensions use the unit last chosen
            }

            ApplyToSelectionWithUndo(s =>
            {
                if (s.Kind == ShapeKind.Dimension)
                {
                    s.DimUnit = option.Value;
                }
            });
        }
    }

    private void DimensionPrecisionTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_boundShape is null || !HasDimensions(_boundShape) || _isRefreshingControls)
        {
            return;
        }

        if (!int.TryParse(DimensionPrecisionTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var places))
        {
            RefreshControlsFromShape();
            return;
        }

        places = Math.Clamp(places, 0, 6);
        if (places != _boundShape.DimPrecision)
        {
            ApplyToSelectionWithUndo(s =>
            {
                if (HasDimensions(s))
                {
                    s.DimPrecision = places;
                }
            });
        }

        RefreshControlsFromShape();
    }

    private void DimensionTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_boundShape is null || _isRefreshingControls)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(DimensionTextBox.Text) ? null : DimensionTextBox.Text;
        if (_boundShape.Kind == ShapeKind.Wall && _dimensionSegment >= 0)
        {
            var segment = _dimensionSegment;
            if (text != WallRenderer.FindOverride(_boundShape, segment)?.Text)
            {
                EditWallDimension(segment, c => c with { Text = text });
            }

            return;
        }

        if (_boundShape.Kind == ShapeKind.Dimension && text != _boundShape.DimText)
        {
            ApplyToSelectionWithUndo(s =>
            {
                if (s.Kind == ShapeKind.Dimension)
                {
                    s.DimText = text;
                }
            });
        }
    }

    private void DimensionOffsetTextBox_OnLostFocus
(object sender, RoutedEventArgs e)
    {
        if (_boundShape is null || !HasDimensions(_boundShape) || _isRefreshingControls)
        {
            return;
        }

        var text = DimensionOffsetTextBox.Text.Trim().Replace(',', '.');
        var perSegment = _boundShape.Kind == ShapeKind.Wall && _dimensionSegment >= 0;
        var currentOffset = perSegment
            ? WallRenderer.FindOverride(_boundShape, _dimensionSegment)?.Offset ?? _boundShape.DimOffset
            : _boundShape.DimOffset;
        if (text == FormatInUnit(currentOffset, _shapeSizeUnit))

        {
            return;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
        {
            RefreshControlsFromShape();
            return;
        }

        var px = UnitToPx(typed, _shapeSizeUnit);
        if (perSegment)
        {
            var segment = _dimensionSegment;
            EditWallDimension(segment, c => c with { Offset = px });
            RefreshControlsFromShape();
            return;
        }

        ApplyToSelectionWithUndo(s =>
        {
            if (HasDimensions(s))
            {
                s.DimOffset = px;

            }
        });
        RefreshControlsFromShape();
    }

    private static readonly ArrowStyleOption[] ArrowStyleOptions =
    [
        new("Arrow (filled)", DimensionArrowStyle.Arrow),
        new("Arrow (open)", DimensionArrowStyle.OpenArrow),
        new("Slash  /", DimensionArrowStyle.Slash),
        new("Backslash  \\", DimensionArrowStyle.Backslash),
        new("Double slash  //", DimensionArrowStyle.DoubleSlash),
        new("Dot", DimensionArrowStyle.Dot),
        new("Ring", DimensionArrowStyle.OpenDot),
        new("Square", DimensionArrowStyle.Box),
        new("None", DimensionArrowStyle.None),
    ];

    private void DimensionArrowStyleComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || DimensionArrowStyleComboBox.SelectedItem is not ArrowStyleOption option)
        {
            return;
        }

        ApplyToSelectionWithUndo(s =>
        {
            if (HasDimensions(s))
            {
                s.DimArrowStyle = option.Value;
            }
        });
    }

    private string? _segmentListKey;
    private int _dimensionSegment = -1;
    private Shape? _dimensionSegmentShape;

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Applies a dimension setting to the picked wall segment, or to every selected dimension / wall otherwise, then refreshes.</summary>
    private void ApplyDimensionSetting(Func<WallDimensionOverride, WallDimensionOverride> perSegment, Action<Shape> perShape)
    {
        if (_boundShape is { Kind: ShapeKind.Wall } && _dimensionSegment >= 0)
        {
            EditWallDimension(_dimensionSegment, perSegment);
        }
        else
        {
            ApplyToSelectionWithUndo(s =>
            {
                if (HasDimensions(s))
                {
                    perShape(s);
                }
            });
        }

        RefreshControlsFromShape();
    }

    /// <summary>Changes the settings of one dimension of every selected wall (one undo step); defaults are dropped again.</summary>
    private void EditWallDimension(int segment, Func<WallDimensionOverride, WallDimensionOverride> change)
    {
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind != ShapeKind.Wall)
            {
                return;
            }

            var list = (s.WallDimensionOverrides ?? []).ToList();
            var current = list.FirstOrDefault(o => o.Segment == segment) ?? new WallDimensionOverride(segment);
            var updated = change(current);
            list.RemoveAll(o => o.Segment == segment);
            if (!updated.IsDefault)
            {
                list.Add(updated);
            }

            s.WallDimensionOverrides = list.Count == 0 ? null : list.OrderBy(o => o.Segment).ToList();
        });
    }

    private void DimensionSegmentComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || DimensionSegmentComboBox.SelectedItem is not SegmentOption option)
        {
            return;
        }

        _dimensionSegment = option.Index;
        RefreshControlsFromShape();
    }

    private void DimensionShowCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingControls || _boundShape is not { Kind: ShapeKind.Wall } || _dimensionSegment < 0)
        {
            return;
        }

        var hidden = DimensionShowCheckBox.IsChecked != true;
        EditWallDimension(_dimensionSegment, c => c with { Hidden = hidden });
        RefreshControlsFromShape();
    }

    private void DimensionFlipCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingControls || _boundShape is not { Kind: ShapeKind.Wall } || _dimensionSegment < 0)
        {
            return;
        }

        var flip = DimensionFlipCheckBox.IsChecked == true;
        EditWallDimension(_dimensionSegment, c => c with { Flip = flip });
    }

    /// <summary>Where the number sits along the dimension line, in percent (0 = start, 50 = middle, 100 = end).</summary>
    private void DimensionTextPositionTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_boundShape is null || !HasDimensions(_boundShape) || _isRefreshingControls)
        {
            return;
        }

        if (!TryParseNumber(DimensionTextPositionTextBox.Text, out var percent))
        {
            RefreshControlsFromShape();
            return;
        }

        var along = Math.Clamp(percent, 0, 100) / 100.0;
        ApplyDimensionSetting(c => c with { TextPosition = along }, s => s.DimTextPosition = along);
    }

    /// <summary>How far the number is moved off the dimension line (px).</summary>
    private void DimensionTextShiftTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_boundShape is null || !HasDimensions(_boundShape) || _isRefreshingControls)
        {
            return;
        }

        if (!TryParseNumber(DimensionTextShiftTextBox.Text, out var shift))
        {
            RefreshControlsFromShape();
            return;
        }

        shift = Math.Clamp(shift, -300, 300);
        ApplyDimensionSetting(c => c with { TextShift = shift }, s => s.DimTextShift = shift);
    }

    /// <summary>Number size, line width, arrowhead size and extension-line gap / overshoot (px).</summary>
    private void DimensionStyleTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_boundShape is null || !HasDimensions(_boundShape) || _isRefreshingControls || sender is not TextBox { Tag: string which } box)
        {
            return;
        }

        if (!double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
        {
            RefreshControlsFromShape();
            return;
        }

        var current = DimensionStyle.For(_boundShape);
        var already = which switch
        {
            "Font" => current.FontSize,
            "Line" => current.LineWidth,
            "Arrow" => current.ArrowSize,
            "Gap" => current.ExtGap,
            _ => current.ExtOvershoot,
        };
        if (Math.Abs(typed - already) < 0.0001)
        {
            return;
        }

        ApplyToSelectionWithUndo(s =>
        {
            if (!HasDimensions(s))
            {
                return;
            }

            switch (which)
            {
                case "Font": s.DimFontSize = Math.Clamp(typed, 4, 200); break;
                case "Line": s.DimLineWidth = Math.Clamp(typed, 0.1, 30); break;
                case "Arrow": s.DimArrowSize = Math.Clamp(typed, 1, 200); break;
                case "Gap": s.DimExtGap = Math.Clamp(typed, 0, 200); break;
                default: s.DimExtOvershoot = Math.Clamp(typed, 0, 200); break;
            }
        });
        RefreshControlsFromShape();
    }

    private void DimensionResetStyleButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyToSelectionWithUndo(s =>
        {
            if (HasDimensions(s))
            {
                s.DimFontSize = null;
                s.DimLineWidth = null;
                s.DimArrowSize = null;
                s.DimExtGap = null;
                s.DimExtOvershoot = null;
            }
        });
        RefreshControlsFromShape();
    }

    private void WallThicknessTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_boundShape is not { Kind: ShapeKind.Wall } wall || _isRefreshingControls)
        {
            return;
        }

        var text = WallThicknessTextBox.Text.Trim().Replace(',', '.');
        if (text == FormatReal(wall.WallThickness))
        {
            return;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed) || typed <= 0)
        {
            RefreshControlsFromShape();
            return;
        }

        var px = Math.Max(0.5, RealToPx(typed));
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind == ShapeKind.Wall)
            {
                s.WallThickness = px;
            }
        });
        RefreshControlsFromShape();
    }

    private void WallTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(Window.GetWindow(this), Window.GetWindow(this));
            e.Handled = true;
        }
    }

    private void WallClosedCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var closed = WallClosedCheckBox.IsChecked == true;
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind == ShapeKind.Wall)
            {
                s.WallClosed = closed && s.Points is { Count: >= 3 };
                WallEditor.PruneOverrides(s); // an opened ring has one segment less
            }
        });
    }

    private void WallHatchComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WallHatchComboBox.SelectedItem is HatchOption option)
        {
            ApplyToSelectionWithUndo(s =>
            {
                if (s.Kind == ShapeKind.Wall)
                {
                    s.Hatch = option.Value;
                }
            });
        }
    }

    private void WallHatchSpacingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WallHatchSpacingValueText is null)
        {
            return;
        }

        WallHatchSpacingValueText.Text = $"{e.NewValue:0}px";
        ApplyToSelection(s =>
        {
            if (s.Kind == ShapeKind.Wall)
            {
                s.HatchSpacing = e.NewValue;
            }
        });
    }

    private void WallHatchColorPicker_OnColorChanged(object? sender, Color color) =>
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind == ShapeKind.Wall)
            {
                s.HatchColor = color.FromMediaColor();
            }
        });

    private void WallDimensionsCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var show = WallDimensionsCheckBox.IsChecked == true;
        ApplyToSelectionWithUndo(s =>
        {
            if (s.Kind == ShapeKind.Wall)
            {
                s.WallShowDimensions = show;
            }
        });
    }

    private void WallUnitComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WallUnitComboBox.SelectedItem is LengthUnitOption option)
        {
            ApplyToSelectionWithUndo(s =>
            {
                if (s.Kind == ShapeKind.Wall)
                {
                    s.WallDimensionUnit = option.Value;
                }
            });
        }
    }

    private void WallSplitAllButton_OnClick(object sender, RoutedEventArgs e) => _context?.SplitSelectedWallsAtAllPoints();

    // ----- Line options (thickness / corners, in cm) -------------------------------------------

    private const double LinePxPerCm = PxPerInch / 2.54;
    private const double DefaultRoundCornerCm = 0.3;

    private static bool IsLineFamily(Shape shape) => ShapeGeometryFactory.IsLineFamily(shape.Kind);

    private void RefreshLineControls(Shape shape)
    {
        LineCard.Visibility = IsLineFamily(shape) ? Visibility.Visible : Visibility.Collapsed;

        var thicknessCm = shape.BorderThickness / LinePxPerCm;
        var radiusCm = shape.CornerRadius / LinePxPerCm;

        LineThicknessSlider.Value = Math.Clamp(thicknessCm, LineThicknessSlider.Minimum, LineThicknessSlider.Maximum);
        LineThicknessTextBox.Text = thicknessCm.ToString("0.00", CultureInfo.InvariantCulture);
        LineCornerRadiusSlider.Value = Math.Clamp(radiusCm, LineCornerRadiusSlider.Minimum, LineCornerRadiusSlider.Maximum);
        LineCornerRadiusTextBox.Text = radiusCm.ToString("0.00", CultureInfo.InvariantCulture);

        if (shape.CornerRadius > 0)
        {
            LineCornerRoundRadio.IsChecked = true;
        }
        else
        {
            LineCornerSharpRadio.IsChecked = true;
        }
    }

    private void LineThicknessSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LineThicknessTextBox is null)
        {
            return;
        }

        LineThicknessTextBox.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        ApplyToSelection(s => s.BorderThickness = e.NewValue * LinePxPerCm);
    }

    private void LineCornerRadiusSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LineCornerRadiusTextBox is null)
        {
            return;
        }

        LineCornerRadiusTextBox.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        ApplyToSelection(s => s.CornerRadius = e.NewValue * LinePxPerCm);
    }

    private void LineCornersRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        if (tag == "Sharp")
        {
            ApplyToSelectionWithUndo(s => s.CornerRadius = 0);
        }
        else
        {
            ApplyToSelectionWithUndo(s =>
            {
                if (s.CornerRadius <= 0)
                {
                    s.CornerRadius = DefaultRoundCornerCm * LinePxPerCm;
                }
            });
        }
    }

    private void LineThicknessTextBox_OnLostFocus(object sender, RoutedEventArgs e) =>
        ApplyLineCmTextBox(LineThicknessTextBox, minCm: 0.01, maxCm: 10, s => s.BorderThickness, (s, px) => s.BorderThickness = px);

    private void LineCornerRadiusTextBox_OnLostFocus(object sender, RoutedEventArgs e) =>
        ApplyLineCmTextBox(LineCornerRadiusTextBox, minCm: 0, maxCm: 20, s => s.CornerRadius, (s, px) => s.CornerRadius = px);

    private void LineTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Moving focus away commits the value through the TextBox's LostFocus handler.
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(Window.GetWindow(this), Window.GetWindow(this));
            e.Handled = true;
        }
    }

    /// <summary>Commits a "value in cm" text field: parses it (comma or dot), clamps it, converts to px and applies
    /// it to the selection as one undo step. Unparsable text snaps back to the shape's current value.</summary>
    private void ApplyLineCmTextBox(TextBox textBox, double minCm, double maxCm, Func<Shape, double> read, Action<Shape, double> write)
    {
        if (_boundShape is null || _isRefreshingControls)
        {
            return;
        }

        var text = textBox.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cm))
        {
            RefreshControlsFromShape();
            return;
        }

        var px = Math.Clamp(cm, minCm, maxCm) * LinePxPerCm;
        if (Math.Abs(px - read(_boundShape)) < 0.0001)
        {
            RefreshControlsFromShape();
            return;
        }

        ApplyToSelectionWithUndo(s => write(s, px));
        RefreshControlsFromShape();
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

    private void RotateResetButton_OnClick(object sender, RoutedEventArgs e) =>
        ApplyToSelectionWithUndo(s => s.Rotation = 0);

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

    private void FillEnabledCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var enabled = FillEnabledCheckBox.IsChecked == true;
        FillColorControls.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ApplyToSelectionWithUndo(s => s.FillEnabled = enabled);
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

    private void ShadowBlurSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ShadowBlurValueText is not null)
        {
            ShadowBlurValueText.Text = $"{e.NewValue:0}px";
        }

        ApplyToSelection(s => s.ShadowBlurRadius = e.NewValue);
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

    private void TextColorPicker_OnColorChanged(object? sender, Color color) =>
        ApplyToSelectionWithUndo(s => s.TextColor = color.FromMediaColor());

    private void TextColorAutoButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyToSelectionWithUndo(s => s.TextColor = null);
        RefreshControlsFromShape();
    }

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

    private void TextUnderlineToggle_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        var underline = TextUnderlineToggle.IsChecked == true;
        ApplyToSelectionWithUndo(s => s.TextUnderline = underline);
    }

    private void FontSizeTextBox_OnLostFocus(object sender, RoutedEventArgs e) => ApplyFontSizeTextBox();

    private void FontSizeTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyFontSizeTextBox();
        }
    }

    private void ApplyFontSizeTextBox()
    {
        if (_boundShape is null || _boundShape.Locked)
        {
            return;
        }

        if (!double.TryParse(FontSizeTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            RefreshControlsFromShape();
            return;
        }

        var before = ShapeSnapshot.Capture(_boundShape);
        _boundShape.TextFontSize = value;
        var after = ShapeSnapshot.Capture(_boundShape);

        if (_context is not null && !after.Equals(before))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([_boundShape], [before], [after]));
        }

        _context?.RequestRedraw();
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
        if (_context is null || _syncingDocumentSettings || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        var mode = Enum.Parse<BackgroundMode>(tag);
        _context.Document.BackgroundMode = mode;
        UpdateBackgroundPanels();

        // Picking "Image" without a picture yet opens the chooser right away.
        if (mode == BackgroundMode.Image && _context.Document.BackgroundImageBytes is not { Length: > 0 })
        {
            ChooseBackgroundImage();
        }

        _context.RequestRedraw();
    }

    // ----- Page background picture ------------------------------------------------------------

    /// <summary>Shows only the settings that belong to the current background mode.</summary>
    private void UpdateBackgroundPanels()
    {
        if (_context is null)
        {
            return;
        }

        var document = _context.Document;
        var mode = document.BackgroundMode;
        var showsPageColor = mode == BackgroundMode.SolidColor
            || (mode == BackgroundMode.Image && document.BackgroundImageFit == BackgroundImageFit.Contain);

        BackgroundColorPicker.Visibility = showsPageColor ? Visibility.Visible : Visibility.Collapsed;
        BackgroundBlurSettingsPanel.Visibility = mode == BackgroundMode.Blur ? Visibility.Visible : Visibility.Collapsed;
        BackgroundImageSettingsPanel.Visibility = mode == BackgroundMode.Image ? Visibility.Visible : Visibility.Collapsed;

        BackgroundImageNameText.Text = document.BackgroundImageBytes is { Length: > 0 }
            ? document.BackgroundImageName ?? "Imported image"
            : "No image chosen";
        BackgroundImageRemoveButton.IsEnabled = document.BackgroundImageBytes is { Length: > 0 };

        switch (document.BackgroundImageFit)
        {
            case BackgroundImageFit.Contain: BgImageFitContainRadio.IsChecked = true; break;
            case BackgroundImageFit.Stretch: BgImageFitStretchRadio.IsChecked = true; break;
            default: BgImageFitCoverRadio.IsChecked = true; break;
        }
    }

    private void BackgroundImageChooseButton_OnClick(object sender, RoutedEventArgs e) => ChooseBackgroundImage();

    private void BackgroundImageRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        _context.Document.BackgroundImageBytes = null;
        _context.Document.BackgroundImageName = null;
        UpdateBackgroundPanels();
        _context.RequestRedraw();
    }

    private void BackgroundImageFitRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_context is null || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        _context.Document.BackgroundImageFit = Enum.Parse<BackgroundImageFit>(tag);
        UpdateBackgroundPanels();
        _context.RequestRedraw();
    }

    /// <summary>Lets the user pick a picture and stores it (re-encoded as PNG, capped to 3000 px on the long side)
    /// as the page background.</summary>
    private void ChooseBackgroundImage()
    {
        if (_context is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico",
            Title = "Choose background image",
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            BitmapSource frame = Path.GetExtension(dialog.FileName).Equals(".ico", StringComparison.OrdinalIgnoreCase)
                ? new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                    .Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First()
                : BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];

            const double maxSide = 3000;
            var longSide = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (longSide > maxSide)
            {
                var scale = maxSide / longSide;
                frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var output = new MemoryStream();
            encoder.Save(output);

            _context.Document.BackgroundImageBytes = output.ToArray();
            _context.Document.BackgroundImageName = Path.GetFileName(dialog.FileName);
        }
        catch (Exception)
        {
            ThemedMessageBox.Show(Window.GetWindow(this), "The file could not be read as an image.",
                "Background image", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdateBackgroundPanels();
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

    private void BackgroundBlurColorPicker_OnColorChanged(object? sender, Color color)
    {
        if (_context is null)
        {
            return;
        }

        _context.Document.BackgroundBlurColor = color.FromMediaColor();
        _context.RequestRedraw();
    }

    private void BackgroundBlurGradientCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        var enabled = BackgroundBlurGradientCheckBox.IsChecked == true;
        _context.Document.BackgroundBlurGradientEnabled = enabled;
        BackgroundBlurGradientColorPicker.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _context.RequestRedraw();
    }

    private void BackgroundBlurGradientColorPicker_OnColorChanged(object? sender, Color color)
    {
        if (_context is null)
        {
            return;
        }

        _context.Document.BackgroundBlurGradientColor = color.FromMediaColor();
        _context.RequestRedraw();
    }

    private void BackgroundBlurAmountSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundBlurAmountValueText is not null)
        {
            BackgroundBlurAmountValueText.Text = $"{e.NewValue:0}%";
        }

        if (_context is null)
        {
            return;
        }

        _context.Document.BackgroundBlurAmount = e.NewValue;
        _context.RequestRedraw();
    }

    private void BackgroundFadeAmountSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundFadeAmountValueText is not null)
        {
            BackgroundFadeAmountValueText.Text = $"{e.NewValue:0}%";
        }

        if (_context is null)
        {
            return;
        }

        _context.Document.BackgroundFadeAmount = e.NewValue;
        _context.RequestRedraw();
    }

    private void BackgroundOpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundOpacityValueText is not null)
        {
            BackgroundOpacityValueText.Text = $"{e.NewValue:0}%";
        }

        if (_context is null)
        {
            return;
        }

        _context.Document.BackgroundOpacity = e.NewValue;
        _context.RequestRedraw();
    }

    // ----- Drawing scale (1:N) -----------------------------------------------------------------------

    private static readonly ScaleOption[] ScaleOptions =
        Document.ScalePresets.Select(d => new ScaleOption(DrawingScale.PresetLabel(d), d)).ToArray();

    private double PageScale => _context?.Document.ScaleDenominator ?? 1;

    /// <summary>A length on the page as the real-world length it stands for at the drawing scale, in the chosen unit.</summary>
    private string FormatReal(double pagePx) => FormatInUnit(pagePx * PageScale, _shapeSizeUnit);

    /// <summary>The page length for a typed real-world length.</summary>
    private double RealToPx(double typed) => UnitToPx(typed, _shapeSizeUnit) / PageScale;

    private void UpdateScaleNote()
    {
        var scale = PageScale;
        ShapeScaleNote.Visibility = Math.Abs(scale - 1) < 1e-9 ? Visibility.Collapsed : Visibility.Visible;
        ShapeScaleNote.Text = $"Real-world sizes at {DrawingScale.Label(scale)}";
    }

    private void OnScaleChanged(double _)
    {
        RefreshDocumentSettings();
        UpdateScaleNote();
        RefreshControlsFromShape();
    }

    private void SyncScaleControls()
    {
        if (_context is null)
        {
            return;
        }

        var scale = _context.Document.ScaleDenominator;
        DocumentScaleComboBox.ItemsSource ??= ScaleOptions;
        DocumentScaleComboBox.SelectedItem = ScaleOptions.FirstOrDefault(o => Math.Abs(o.Value - scale) < 1e-9);
        DocumentScaleTextBox.Text = scale.ToString("0.##", CultureInfo.InvariantCulture);
        DocumentScaleNote.Text = Math.Abs(scale - 1) < 1e-9
            ? "Full size: what you draw is what it measures."
            : $"On the page 1 cm stands for {scale:0.##} cm in reality. Measurements and wall sizes are written in real-world units; print at 100% for an exact {DrawingScale.Label(scale)} plot.";
    }

    private void DocumentScaleComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_context is null || _syncingDocumentSettings || DocumentScaleComboBox.SelectedItem is not ScaleOption option)
        {
            return;
        }

        _context.SetScale(option.Value);
    }

    private void DocumentScaleTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_context is null || _syncingDocumentSettings)
        {
            return;
        }

        if (double.TryParse(DocumentScaleTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            _context.SetScale(value);
        }

        SyncScaleControls();
    }

    // ----- Sheet (frame and title block) -------------------------------------------------------------

    private void SyncSheetControls()
    {
        if (_context is null)
        {
            return;
        }

        var sheet = _context.Document.Sheet;
        SheetEnabledCheckBox.IsChecked = sheet.Enabled;
        SheetProjectTextBox.Text = sheet.Project;
        SheetAddressTextBox.Text = sheet.Address;
        SheetDateTextBox.Text = sheet.Date;
        SheetPositionTextBox.Text = sheet.Position;
        SheetScaleText.Text = $"Scale: {DrawingScale.Label(_context.Document.ScaleDenominator)} (from the page scale)";
        SheetPlaceBottomRadio.IsChecked = sheet.Placement == SheetPlacement.Bottom;
        SheetPlaceTopRadio.IsChecked = sheet.Placement == SheetPlacement.Top;
        SheetPlaceLeftRadio.IsChecked = sheet.Placement == SheetPlacement.Left;
        SheetPlaceRightRadio.IsChecked = sheet.Placement == SheetPlacement.Right;
        SheetMarginTextBox.Text = sheet.MarginMm.ToString("0.##", CultureInfo.InvariantCulture);
        SheetBlockSizeTextBox.Text = sheet.BlockSizeMm > 0 ? sheet.BlockSizeMm.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
        SheetBlockSizeTextBox.ToolTip = $"Height (top / bottom) or width (left / right) of the title block in mm; empty = automatic ({SheetInfo.AutomaticBlockSizeMm(sheet.Placement)} mm)";
    }

    private void SheetEnabledCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || _syncingDocumentSettings)
        {
            return;
        }

        // ToggleSheet also fills an empty date; the checkbox has already flipped itself, so only act when the state differs
        if (_context.Document.Sheet.Enabled != (SheetEnabledCheckBox.IsChecked == true))
        {
            _context.ToggleSheet();
        }
    }

    private void SheetTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void SheetTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_context is null || _syncingDocumentSettings || sender is not TextBox { Tag: string field } box)
        {
            return;
        }

        var sheet = _context.Document.Sheet;
        var text = box.Text.Trim();
        var number = double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null;

        var changed = field switch
        {
            "Project" => sheet with { Project = text },
            "Address" => sheet with { Address = text },
            "Date" => sheet with { Date = text },
            "Position" => sheet with { Position = text },
            "Margin" when number is >= 0 and <= 100 => sheet with { MarginMm = number.Value },
            "Block" when text.Length == 0 => sheet with { BlockSizeMm = 0 },
            "Block" when number is > 0 and <= 400 => sheet with { BlockSizeMm = number.Value },
            _ => sheet,
        };

        _context.SetSheet(changed);
        SyncSheetControlsGuarded();
    }

    private void SheetTodayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_context is not null)
        {
            _context.SetSheet(_context.Document.Sheet with { Date = EditorContext.TodayText() });
        }
    }

    private void SheetPlacementRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_context is null || _syncingDocumentSettings || sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse<SheetPlacement>(tag, out var placement))
        {
            return;
        }

        _context.SetSheet(_context.Document.Sheet with { Placement = placement });
    }

    /// <summary>Puts the stored values back into the boxes (a refused entry such as text in a number box shows the old value again).</summary>
    private void SyncSheetControlsGuarded()
    {
        _syncingDocumentSettings = true;
        try
        {
            SyncSheetControls();
        }
        finally
        {
            _syncingDocumentSettings = false;
        }
    }

    private enum CanvasSizeUnit { Px, Cm, Mm, In }

    private const double PxPerInch = 96.0;
    private const double MmPerInch = 25.4;

    private CanvasSizeUnit _canvasSizeUnit = CanvasSizeUnit.Px;

    /// <summary>Unit of the selected shape's size/position boxes (SIZE &amp; POSITION card); values stay stored in px.</summary>
    private CanvasSizeUnit _shapeSizeUnit = CanvasSizeUnit.Px;

    private static double PxToUnit(double px, CanvasSizeUnit unit) => unit switch
    {
        CanvasSizeUnit.Cm => px / PxPerInch * (MmPerInch / 10),
        CanvasSizeUnit.Mm => px / PxPerInch * MmPerInch,
        CanvasSizeUnit.In => px / PxPerInch,
        _ => px,
    };

    private static double UnitToPx(double value, CanvasSizeUnit unit) => unit switch
    {
        CanvasSizeUnit.Cm => value / (MmPerInch / 10) * PxPerInch,
        CanvasSizeUnit.Mm => value / MmPerInch * PxPerInch,
        CanvasSizeUnit.In => value * PxPerInch,
        _ => value,
    };

    private static string UnitFormat(CanvasSizeUnit unit) => unit switch
    {
        CanvasSizeUnit.Px => "0",
        CanvasSizeUnit.In => "0.###",
        _ => "0.##",
    };

    private static string FormatInUnit(double px, CanvasSizeUnit unit) =>
        PxToUnit(px, unit).ToString(UnitFormat(unit), CultureInfo.InvariantCulture);

    private void RefreshCanvasSizeTextBoxes()
    {
        if (_context is null)
        {
            return;
        }

        CanvasWidthTextBox.Text = FormatInUnit(_context.Document.Width, _canvasSizeUnit);
        CanvasHeightTextBox.Text = FormatInUnit(_context.Document.Height, _canvasSizeUnit);

        _syncingOrientation = true;
        try
        {
            var landscape = _context.Document.Width > _context.Document.Height;
            PagePortraitRadio.IsChecked = !landscape;
            PageLandscapeRadio.IsChecked = landscape;
        }
        finally
        {
            _syncingOrientation = false;
        }
    }

    private bool _syncingOrientation;

    /// <summary>Portrait / Landscape: turns the page by swapping its width and height (the drawing stays where it is).</summary>
    private void PageOrientationRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!_syncingOrientation && sender is RadioButton { Tag: string tag })
        {
            SetPageOrientation(tag == "Landscape");
        }
    }

    /// <summary>Turns the page to landscape or portrait (swaps width and height). Used by the card and the Page menu.</summary>
    public void SetPageOrientation(bool landscape)
    {
        if (_context is null)
        {
            return;
        }

        var document = _context.Document;
        if (landscape == (document.Width > document.Height) || document.Width == document.Height)
        {
            RefreshCanvasSizeTextBoxes();
            return;
        }

        ApplyCanvasSize(document.Height, document.Width);
    }

    private void ShapeSizeUnitRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        _shapeSizeUnit = Enum.Parse<CanvasSizeUnit>(tag);
        if (_boundShape is not null)
        {
            RefreshControlsFromShape();
        }
    }

    private void CanvasSizeUnitRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        _canvasSizeUnit = Enum.Parse<CanvasSizeUnit>(tag);
        RefreshCanvasSizeTextBoxes();
    }

    /// <summary>"NNpx" presets are plain pixel squares; the paper presets are converted from their
    /// real-world mm size at <see cref="PxPerInch"/> (96 DPI — the standard CSS/screen mapping).</summary>
    private void CanvasSizePreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            ApplyPagePreset(tag);
        }
    }

    /// <summary>Sets the page to a named preset ("A4", "128px" ...), keeping its current orientation. Used by the card and the Page menu.</summary>
    public void ApplyPagePreset(string tag)
    {
        if (_context is null)
        {
            return;
        }

        (int WidthPx, int HeightPx)? size = tag switch
        {
            "64px" => (64, 64),
            "128px" => (128, 128),
            "256px" => (256, 256),
            "512px" => (512, 512),
            "A5" => ((int)Math.Round(UnitToPx(148, CanvasSizeUnit.Mm)), (int)Math.Round(UnitToPx(210, CanvasSizeUnit.Mm))),
            "A4" => ((int)Math.Round(UnitToPx(210, CanvasSizeUnit.Mm)), (int)Math.Round(UnitToPx(297, CanvasSizeUnit.Mm))),
            "A3" => ((int)Math.Round(UnitToPx(297, CanvasSizeUnit.Mm)), (int)Math.Round(UnitToPx(420, CanvasSizeUnit.Mm))),
            "Letter" => ((int)Math.Round(UnitToPx(215.9, CanvasSizeUnit.Mm)), (int)Math.Round(UnitToPx(279.4, CanvasSizeUnit.Mm))),
            _ => null,
        };

        if (size is not { } s)
        {
            return;
        }

        // a paper preset keeps the orientation the page has now
        var keepLandscape = _context.Document.Width > _context.Document.Height;
        ApplyCanvasSize(keepLandscape ? s.HeightPx : s.WidthPx, keepLandscape ? s.WidthPx : s.HeightPx);
    }

    private void CanvasSizeTextBox_OnLostFocus(object sender, RoutedEventArgs e) => ApplyCanvasSizeTextBoxes();

    private void CanvasSizeTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyCanvasSizeTextBoxes();
        }
    }

    private void ApplyCanvasSizeTextBoxes()
    {
        if (_context is null)
        {
            return;
        }

        if (!double.TryParse(CanvasWidthTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var widthValue) ||
            !double.TryParse(CanvasHeightTextBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var heightValue) ||
            widthValue <= 0 || heightValue <= 0)
        {
            RefreshCanvasSizeTextBoxes();
            return;
        }

        ApplyCanvasSize(
            (int)Math.Round(UnitToPx(widthValue, _canvasSizeUnit)),
            (int)Math.Round(UnitToPx(heightValue, _canvasSizeUnit)));
    }

    private void ApplyCanvasSize(int widthPx, int heightPx)
    {
        if (_context is null)
        {
            return;
        }

        _context.Document.Width = Math.Clamp(widthPx, 1, 10000);
        _context.Document.Height = Math.Clamp(heightPx, 1, 10000);
        RefreshCanvasSizeTextBoxes();
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

    private void LayerAccentSwatch_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Layer layer } element)
        {
            return;
        }

        _accentColorTargetLayer = layer;
        LayerAccentColorPopup.PlacementTarget = element;
        LayerAccentColorPopup.IsOpen = true;
    }

    private void LayerAccentColorSwatch_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_accentColorTargetLayer is not { } layer || sender is not Border { Background: SolidColorBrush brush })
        {
            return;
        }

        layer.AccentColor = brush.Color.FromMediaColor();
        LayerAccentColorPopup.IsOpen = false;
        RebuildLayerList();
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
