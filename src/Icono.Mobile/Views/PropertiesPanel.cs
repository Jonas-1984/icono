using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>The properties of the selection, on the right of the tablet app. It shows what fits the selection: nothing selected - the drawing
/// scale; shapes - fill, line colour and width; walls - thickness, hatch, dimensions; doors and windows - width; dimensions - unit and
/// decimals; always turn and flip. Every change is one undo step (<see cref="EditorContext.SetOnSelection"/>). Lengths are shown as in reality
/// (at the drawing scale), line widths as on the paper.</summary>
public sealed partial class PropertiesPanel : UserControl
{
    private const double PxPerCm = 96 / 2.54;

    private static readonly ArgbColor[] Swatches =
    [
        ArgbColor.FromRgb(0xFF, 0xFF, 0xFF), ArgbColor.FromRgb(0xD9, 0xDE, 0xE6), ArgbColor.FromRgb(0x8A, 0x94, 0xA6), ArgbColor.FromRgb(0x1B, 0x23, 0x32),
        ArgbColor.FromRgb(0xE5, 0x48, 0x4D), ArgbColor.FromRgb(0xF5, 0x9E, 0x0B), ArgbColor.FromRgb(0xFA, 0xCC, 0x15), ArgbColor.FromRgb(0x22, 0xC5, 0x5E),
        ArgbColor.FromRgb(0x9E, 0xD9, 0xD1), ArgbColor.FromRgb(0x3B, 0x82, 0xF6), ArgbColor.FromRgb(0x8B, 0x5C, 0xF6), ArgbColor.FromRgb(0xEC, 0x48, 0x99),
    ];

    private static readonly (string Name, LengthUnit Unit)[] Units =
        [("mm", LengthUnit.Millimeter), ("cm", LengthUnit.Centimeter), ("m", LengthUnit.Meter), ("in", LengthUnit.Inch)];

    private readonly StackPanel _content = new() { Spacing = 6, Margin = new Thickness(12, 10, 12, 16) };
    private EditorContext? _context;
    private bool _rebuildQueued;

    /// <summary>Which of the two tabs is shown: the properties of the selection, or the pages and layers of the drawing.</summary>
    private enum Tab { Properties, Page, Layers, Snap }

    private Tab _tab = Tab.Properties;

    private readonly Button _propertiesTab;
    private readonly Button _pageTab;
    private readonly Button _layersTab;
    private readonly Button _snapTab;

    public PropertiesPanel()
    {
        _propertiesTab = TabButton("Properties", () => ShowTab(Tab.Properties));
        _pageTab = TabButton("Page", () => ShowTab(Tab.Page));
        _layersTab = TabButton("Layers", () => ShowTab(Tab.Layers));
        _snapTab = TabButton("Snap", () => ShowTab(Tab.Snap));
        // Properties is the longest name: its tab is a little wider than the other three
        var tabGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,*,*,*") };
        var tabKeys = new[] { _propertiesTab, _pageTab, _layersTab, _snapTab };
        for (var i = 0; i < tabKeys.Length; i++)
        {
            Grid.SetColumn(tabKeys[i], i);
            tabGrid.Children.Add(tabKeys[i]);
        }
        var tabs = new Border { Classes = { "segPlate" }, Margin = new Thickness(10, 8, 10, 2), Child = tabGrid };

        var layout = new DockPanel();
        DockPanel.SetDock(tabs, Dock.Top);
        layout.Children.Add(tabs);
        _scroll = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        layout.Children.Add(_scroll);
        Content = layout;
        ShowTab(Tab.Properties);
    }

    public EditorContext? Context
    {
        get => _context;
        set
        {
            if (_context is not null)
            {
                _context.SelectedShapes.CollectionChanged -= OnChanged;
                _context.UndoManager.PropertyChanged -= OnChanged;
                _context.LayersChanged -= OnChanged;
                _context.PagesChanged -= OnChanged;
                _context.CurrentPageChanged -= OnChanged;
                _context.PropertyChanged -= OnChanged;
                _context.PointSnap.PropertyChanged -= OnPointSnapChanged;
            }

            _context = value;
            if (_context is not null)
            {
                _context.SelectedShapes.CollectionChanged += OnChanged;
                _context.UndoManager.PropertyChanged += OnChanged;
                _context.LayersChanged += OnChanged;
                _context.PagesChanged += OnChanged;
                _context.CurrentPageChanged += OnChanged;
                _context.PropertyChanged += OnChanged; // the tool (the Pen shows its settings), the pen colour and width ...
                _context.PointSnap.PropertyChanged += OnPointSnapChanged;
            }

            Rebuild();
        }
    }

    private void OnChanged() => OnChanged(null, EventArgs.Empty);

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppTheme.Changed += OnChanged; // controls built in code take their colours from the look at the time they are built
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AppTheme.Changed -= OnChanged;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>OSnap in the status line (or another place) switched the object snap: the Snap tab shows it.</summary>
    private void OnPointSnapChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_tab == Tab.Snap && e.PropertyName == nameof(Icono.Rendering.PointSnapOptions.Enabled))
        {
            OnChanged();
        }
    }

    private void ShowTab(Tab tab)
    {
        _tab = tab;
        _propertiesTab.Classes.Set("on", tab == Tab.Properties);
        _pageTab.Classes.Set("on", tab == Tab.Page);
        _layersTab.Classes.Set("on", tab == Tab.Layers);
        _snapTab.Classes.Set("on", tab == Tab.Snap);
        Rebuild();
    }

    /// <summary>Shows the PAGE tab (the page size, the sheet, the background ...).</summary>
    public void ShowPageSettings() => ShowTab(Tab.Page);

    /// <summary>Shows the SNAP tab (grid, object snap, aligning, the helpers of the wall tool).</summary>
    public void ShowSnapSettings() => ShowTab(Tab.Snap);

    private Button TabButton(string text, Action click)
    {
        var button = new Button
        {
            Content = text, Classes = { "seg" },
        };
        button.Click += (_, _) => click();
        return button;
    }

    private double Scale => _context?.Document.ScaleDenominator is > 0 and var s ? s : 1;

    /// <summary>A length on the paper (drawing units) as real-world centimetres at the drawing scale, and back.</summary>
    private double ToRealCm(double px) => px / PxPerCm * Scale;

    private double FromRealCm(double cm) => cm * PxPerCm / Scale;

    /// <summary>The unit the sizes are shown in: the unit of the canvas (CANVAS SIZE: mm, cm or inch), as in the Windows program.</summary>
    private double UnitFactor => _context?.DimensionUnit switch { LengthUnit.Millimeter => 10, LengthUnit.Inch => 1 / 2.54, LengthUnit.Meter => 0.01, _ => 1 };

    private string UnitName => _context?.DimensionUnit switch { LengthUnit.Millimeter => "mm", LengthUnit.Inch => "in", LengthUnit.Meter => "m", _ => "cm" };

    private double ToReal(double px) => ToRealCm(px) * UnitFactor;

    private double FromReal(double value) => FromRealCm(value / UnitFactor);

    /// <summary>A length of the drawing (page pixels) as a number box in the unit of the canvas at the drawing scale. The step and the limits are
    /// given in cm.</summary>
    private void RealRow(string label, double px, double stepCm, double minCm, double maxCm, Action<double> setPx, string format = "0.##") =>
        NumberRow(label, ToReal(px), UnitName, stepCm * UnitFactor, minCm * UnitFactor, maxCm * UnitFactor, format, v => setPx(FromReal(v)));

    private void OnChanged(object? sender, EventArgs e)
    {
        // several changes in a row (a selection of a group, an undo) rebuild once
        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        });
    }

    // ---------------------------------------------------------------- building the panel

    private void Rebuild()
    {
        _content.Children.Clear();
        _into = _content;
        if (_context is not { } context)
        {
            return;
        }

        context.WallSegmentHighlight = null; // BuildDimension sets it again for a wall whose dimension is being edited
        if (_tab == Tab.Layers)
        {
            BuildPagesAndLayers(context);
            return;
        }

        if (_tab == Tab.Page)
        {
            BuildPage(context);
            return;
        }

        if (_tab == Tab.Snap)
        {
            Title("Snapping");
            Note("What the points and lines you draw or move jump to. OSnap, Snap and Grid in the status line switch the main ones on and off.");
            BuildSnapping(context);
            return;
        }

        if (BuildToolOptions(context))
        {
            return;
        }

        // while the Pen is the tool its settings are shown (colour, width, smoothing)
        if (context.ActiveTool == ActiveTool.Freehand)
        {
            BuildPen(context);
            return;
        }

        var shapes = context.SelectedShapes.ToList();
        if (shapes.Count == 0)
        {
            BuildDrawing(context);
            return;
        }

        var first = shapes[0];
        Title(shapes.Count == 1 ? Describe(first) : $"{shapes.Count} objects");
        if (shapes.All(s => s.Locked))
        {
            Note("Locked - unlock it with a long press to change it.");
            return;
        }

        var walls = shapes.Where(s => s.Kind == ShapeKind.Wall).ToList();
        var symbols = shapes.Where(s => ArchitecturalSymbols.IsSymbol(s.Kind)).ToList();
        var dimensions = shapes.Where(s => s.Kind == ShapeKind.Dimension).ToList();
        var filled = shapes.Where(HasFill).ToList();
        var outlined = shapes.Where(s => s.Kind is not (ShapeKind.Dimension or ShapeKind.Wall or ShapeKind.Text)).ToList();

        BuildText(context, shapes);

        if (walls.Count > 0)
        {
            BuildWall(context, walls[0]);
        }

        if (symbols.Count == 1 && shapes.Count == 1)
        {
            BuildSymbol(context, symbols[0]);
        }

        BuildDimension(context, shapes);

        if (filled.Count > 0)
        {
            Heading("FILL");
            var fillOn = filled[0].FillEnabled;
            Toggle("Fill", fillOn, on => context.SetOnSelection(s => s.FillEnabled = on, HasFill));
            if (fillOn)
            {
                ColorRow(filled[0].FillColor, c => context.SetOnSelection(s =>
                {
                    s.FillEnabled = true;
                    s.FillColor = c;
                }, HasFill));
            }
        }

        BuildDesignGrid(context, shapes);
        BuildDesignStyle(context, shapes);

        if (outlined.Count > 0)
        {
            BuildBorder(context, outlined);
        }

        BuildEffects(context, shapes);
        BuildLineEnds(context, shapes);
        BuildPosition(context, shapes);

        BuildArrange(context, shapes);
    }

    /// <summary>Raised by "Edit text": the window opens the text to type for this shape.</summary>
    public event EventHandler<Shape>? EditTextRequested;

    /// <summary>Text objects and the text inside shapes: edit, size (pt on the paper), bold, italic, colour.</summary>
    private void BuildText(EditorContext context, IReadOnlyList<Shape> shapes)
    {
        var withText = shapes.Where(s => s.Kind == ShapeKind.Text || ShapeLabel.HasLabel(s)).ToList();
        var single = shapes.Count == 1 && DocumentCanvas.CanEditText(shapes[0]) ? shapes[0] : null;
        if (withText.Count == 0 && single is null)
        {
            return;
        }

        Heading("TEXT");
        if (single is not null)
        {
            ButtonRow((string.IsNullOrEmpty(single.TextContent) ? "Add text" : "Edit text", () => EditTextRequested?.Invoke(this, single)));
        }

        if (withText.Count == 0)
        {
            return;
        }

        bool HasText(Shape s) => s.Kind == ShapeKind.Text || ShapeLabel.HasLabel(s);
        var sample = withText[0];

        // page pixels (96 per inch) as points (72 per inch)
        NumberRow("Size", sample.TextFontSize * 0.75, "pt", 1, 4, 400, "0.#",
            pt => context.SetOnSelection(s => s.TextFontSize = pt / 0.75, HasText));
        Toggle("Bold", sample.TextBold, on => context.SetOnSelection(s => s.TextBold = on, HasText));
        Toggle("Italic", sample.TextItalic, on => context.SetOnSelection(s => s.TextItalic = on, HasText));
        BuildTextFormat(context, withText, HasText);

        // a text object is drawn in its fill colour, the text in a shape in its own text colour
        var color = sample.Kind == ShapeKind.Text ? sample.FillColor : ShapeLabel.EffectiveColor(sample);
        ColorRow(color, c => context.SetOnSelection(s =>
        {
            if (s.Kind == ShapeKind.Text)
            {
                s.FillColor = c;
            }
            else
            {
                s.TextColor = c;
            }
        }, HasText));
        if (withText.Any(s => s.Kind != ShapeKind.Text && s.TextColor is not null))
        {
            ButtonRow(("Automatic text colour", () => context.SetOnSelection(s => s.TextColor = null, s => HasText(s) && s.Kind != ShapeKind.Text)));
        }
    }

    /// <summary>The page and its settings (the PAGE tab): scale, canvas size and unit, rulers, sheet, background, grid and snapping.</summary>
    private void BuildPage(EditorContext context)
    {
        Title("Page");
        Heading("SCALE");
        var presets = Document.ScalePresets.ToList();
        if (!presets.Contains(context.Document.ScaleDenominator))
        {
            presets.Add(context.Document.ScaleDenominator);
            presets.Sort();
        }

        var combo = new ComboBox
        {
            ItemsSource = presets.Select(p => "1:" + p.ToString("0.##", CultureInfo.InvariantCulture)).ToList(),
            SelectedIndex = presets.IndexOf(context.Document.ScaleDenominator),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && Math.Abs(presets[combo.SelectedIndex] - context.Document.ScaleDenominator) > 1e-9)
            {
                context.SetScale(presets[combo.SelectedIndex]);
            }
        };
        _into.Children.Add(combo);
        Note("The drawing is resized with the scale, so all dimensions keep their values.");

        var doc = context.Document;
        Heading("PAGE");
        Note($"{doc.Width / PxPerCm * 10:0} x {doc.Height / PxPerCm * 10:0} mm, {doc.PageCount} page(s), " +
             $"{doc.Layers.Sum(l => l.Shapes.Count)} object(s)");
        BuildCanvasSize(context);
        Toggle("Show the rulers", context.ShowRulers, on =>
        {
            context.ShowRulers = on;
            context.RequestRedraw();
        });
        BuildSheet(context);
        BuildBackground(context);
    }

    private static bool HasFill(Shape shape) =>
        shape.Kind is not (ShapeKind.Wall or ShapeKind.Dimension or ShapeKind.Text)
        && !ShapeGeometryFactory.IsLineFamily(shape.Kind)
        && !ArchitecturalSymbols.IsSymbol(shape.Kind);

    private static string Describe(Shape shape) => !string.IsNullOrWhiteSpace(shape.Name) ? shape.Name : Words(shape.Kind.ToString());

    private static string Words(string name) => System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");

    // ---------------------------------------------------------------- pieces

    private void Title(string text) => _content.Children.Add(new TextBlock
    {
        Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brush("ForegroundTextBrush"), Margin = new Thickness(0, 0, 0, 2),
        TextTrimming = TextTrimming.CharacterEllipsis,
    });

    /// <summary>The panel adds its controls to this: the body of the section that was started last (see <see cref="Heading"/>).</summary>
    private Panel _into = null!;

    /// <summary>The sections that are folded in (the rest is open): kept while the panel is built again.</summary>
    private readonly HashSet<string> _closedSections =
    [
        "MOVE", "ALIGN", "ALIGN TO PAGE", "COMBINE", "ADD DIMENSIONS", "DOOR & WINDOW MEASURES", "ALIGN WHILE MOVING", "WALL DRAWING",
        "OPACITY, BLUR & SHADOW", "TEXT POSITION",
    ];

    /// <summary>Starts a section: its name is a button that folds the controls below it in and out (until the next section starts).</summary>
    /// <summary>The header button of the section that was started last (to scroll to it).</summary>
    private Button? _lastHeader;

    /// <summary>Brings a section header into view after the panel is built (a tapped dimension number shows its settings).</summary>
    private void ScrollTo(Control? target) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // after the panel is laid out: the section header goes to the top of the view
            if (target is null || _scroll is null)
            {
                return;
            }

            _scroll.UpdateLayout();
            if (target.TranslatePoint(new Point(0, 0), _content) is { } at)
            {
                _scroll.Offset = new Vector(0, Math.Max(0, at.Y - 4));
            }
        }, Avalonia.Threading.DispatcherPriority.Background);

    private readonly ScrollViewer _scroll;

    private void Heading(string text)
    {
        var body = new StackPanel { Spacing = 6 };
        var card = new Border { Classes = { "card" }, Child = body, IsVisible = !_closedSections.Contains(text) };
        var chevron = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M2,4 L6,8 L10,4"), Stroke = Brush("MutedTextBrush"), StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round, Width = 12, Height = 12, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
        };
        var header = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent,
            Padding = new Thickness(4, 8, 4, 5), Margin = new Thickness(0, 4, 0, 0), FontSize = 10.5, FontWeight = FontWeight.SemiBold,
            Foreground = Brush("MutedTextBrush"),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 7,
                Children = { chevron, new TextBlock { Text = text, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center } },
            },
        };

        void Show() => chevron.RenderTransform = card.IsVisible ? null : new RotateTransform(-90);
        header.Click += (_, _) =>
        {
            card.IsVisible = !card.IsVisible;
            if (card.IsVisible)
            {
                _closedSections.Remove(text);
            }
            else
            {
                _closedSections.Add(text);
            }

            Show();
        };
        Show();
        _content.Children.Add(header);
        _content.Children.Add(card);
        _into = body;
        _lastHeader = header;
    }

    private void Note(string text) => _into.Children.Add(new TextBlock
    {
        Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedTextBrush"),
    });

    private void ColorRow(ArgbColor current, Action<ArgbColor> choose, IReadOnlyList<ArgbColor>? palette = null, bool hexEntry = true)
    {
        var grid = new UniformGrid { Columns = 6 };
        foreach (var swatch in palette ?? Swatches)
        {
            var isCurrent = swatch.R == current.R && swatch.G == current.G && swatch.B == current.B;
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromRgb(swatch.R, swatch.G, swatch.B)),
                BorderBrush = isCurrent ? Brush("AccentBrush") : Brush("FieldEdgeBrush"),
                BorderThickness = new Thickness(isCurrent ? 3 : 1),
            };
            button.Click += (_, _) => choose(swatch);
            grid.Children.Add(button);
        }

        _into.Children.Add(grid);
        if (!hexEntry)
        {
            return;
        }

        // any colour: type it as #RRGGBB (the box shows the current one)
        var box = new TextBox
        {
            Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}", FontSize = 13, MinHeight = 28, Padding = new Thickness(6, 0), Classes = { "field" },
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };

        void Commit()
        {
            var text = (box.Text ?? string.Empty).Trim().TrimStart('#');
            if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                var typed = ArgbColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                if (typed.R != current.R || typed.G != current.G || typed.B != current.B)
                {
                    choose(typed);
                    return;
                }
            }

            box.Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}";
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };

        // the paletteButton: a tap opens a square and a bar to choose any colour with the finger; the colour is taken when the paletteButton is closed
        // the colour as a key as high as the hex box next to it; the arrow is dark on a light colour and white on a dark one
        var light = (0.299 * current.R + 0.587 * current.G + 0.114 * current.B) / 255 > 0.6;
        var paletteButton = new Button
        {
            Width = 44, Height = 28, MinHeight = 28, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(10), Classes = { "bare" },
            Content = new Border
            {
                CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(current.R, current.G, current.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M2,4 L6,8 L10,4"), StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                    Width = 12, Height = 12, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Stroke = light ? new SolidColorBrush(Color.Parse("#FF082A52")) : Brushes.White,
                },
            },
        };
        paletteButton.Click += (_, _) =>
        {
            var pad = new ColorPickerPad(current, palette ?? Swatches);
            var flyout = new Flyout { Content = pad, Placement = PlacementMode.LeftEdgeAlignedTop };
            flyout.FlyoutPresenterClasses.Add("numpad"); // see-through around the glass of the pad
            pad.Done += flyout.Hide;
            flyout.Closed += (_, _) =>
            {
                var chosen = pad.Chosen;
                if (chosen.R != current.R || chosen.G != current.G || chosen.B != current.B)
                {
                    choose(chosen);
                }
            };
            flyout.ShowAt(paletteButton);
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*,Auto"), Margin = new Thickness(0, 2) };
        row.Children.Add(Label("Colour #"));
        Grid.SetColumn(box, 1);
        Grid.SetColumn(paletteButton, 2);
        row.Children.Add(box);
        row.Children.Add(paletteButton);
        _into.Children.Add(row);
    }

    /// <summary>A value with - and + (one step each) and a box to type it in; the value is applied on Enter or when the box loses focus.</summary>
    private void NumberRow(string label, double value, string unit, double step, double min, double max, string format, Action<double> set)
    {
        string Show(double v) => v.ToString(format, CultureInfo.InvariantCulture);

        // the box shows the number; a tap opens a number pad of frosted glass (not the keyboard of the tablet)
        var box = new TextBox
        {
            Text = Show(value), FontSize = 13, MinHeight = 28, Padding = new Thickness(6, 0), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, IsReadOnly = true, Focusable = false, Classes = { "field" },
        };

        void Commit(double v)
        {
            v = Math.Clamp(Math.Round(v / step) * step, min, max);
            if (Math.Abs(v - value) > 1e-9)
            {
                set(v);
            }
            else
            {
                box.Text = Show(value);
            }
        }

        void CommitTyped(string text)
        {
            if (double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
            {
                // typed values are taken as they are (only kept inside the limits), the buttons go in steps
                typed = Math.Clamp(typed, min, max);
                if (Math.Abs(typed - value) > 1e-9)
                {
                    set(typed);
                    return;
                }
            }

            box.Text = Show(value);
        }

        box.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            e.Handled = true;
            var pad = new NumberPad(label, unit, Show(value));
            var flyout = new Flyout { Content = pad, Placement = PlacementMode.LeftEdgeAlignedTop };
            flyout.FlyoutPresenterClasses.Add("numpad");
            pad.Committed += text =>
            {
                flyout.Hide();
                CommitTyped(text);
            };
            pad.Cancelled += flyout.Hide;
            flyout.ShowAt(box);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var minus = StepButton("−");
        minus.Click += (_, _) => Commit(value - step);
        var plus = StepButton("+");
        plus.Click += (_, _) => Commit(value + step);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,34,*,34,30"), Margin = new Thickness(0, 2) };
        row.Children.Add(Label(label));
        Grid.SetColumn(minus, 1);
        Grid.SetColumn(box, 2);
        Grid.SetColumn(plus, 3);
        var unitText = Label(unit);
        unitText.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(unitText, 4);
        row.Children.Add(minus);
        row.Children.Add(box);
        row.Children.Add(plus);
        row.Children.Add(unitText);

        // under the number: a slider for 0 to 20 (finer values by the number box); larger numbers are typed into the box
        var tick = step >= 1 ? 1.0 : 0.1; // whole-number rows slide in whole steps
        var low = tick >= 1 ? Math.Ceiling(Math.Max(min, 0)) : Math.Max(min, 0);
        var high = Math.Min(max, 20);
        if (low >= high)
        {
            _into.Children.Add(row);
            return;
        }

        var slider = new Slider
        {
            Minimum = low, Maximum = high, Value = Math.Round(Math.Clamp(value, low, high) / tick) * tick, TickFrequency = tick, IsSnapToTickEnabled = true,
            Classes = { "thin" }, Margin = new Thickness(78, -4, 30, 0), Opacity = value >= low && value <= high ? 1 : 0.4,
        };
        var sliding = false;
        slider.PropertyChanged += (_, e) =>
        {
            // while the finger is on the slider only the number moves; the change is made (one undo step) when the finger lifts
            if (e.Property == RangeBase.ValueProperty && sliding)
            {
                box.Text = Show(Math.Round(slider.Value / tick) * tick);
            }
        };
        // a finger that starts on the slider to scroll the panel (it moves up or down, not along the slider) must not change the number: that is a scroll
        Point? pressedAt = null;
        double moveAlong = 0, moveAcross = 0;
        var startValue = slider.Value;
        slider.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            sliding = true;
            pressedAt = e.GetPosition(slider);
            moveAlong = moveAcross = 0;
            startValue = slider.Value;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (sliding && pressedAt is { } start)
            {
                var now = e.GetPosition(slider);
                moveAlong = Math.Max(moveAlong, Math.Abs(now.X - start.X));
                moveAcross = Math.Max(moveAcross, Math.Abs(now.Y - start.Y));
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        void Release()
        {
            if (!sliding)
            {
                return;
            }

            sliding = false;
            if (moveAcross > 12 && moveAcross > moveAlong)
            {
                // it was a scroll of the panel: the slider and the number go back, nothing is changed
                slider.Value = startValue;
                box.Text = Show(value);
                return;
            }

            CommitTyped((Math.Round(slider.Value / tick) * tick).ToString("0.##", CultureInfo.InvariantCulture));
        }

        slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Release(), Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        slider.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Release(), Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        var both = new StackPanel { Spacing = 0 };
        both.Children.Add(row);
        both.Children.Add(slider);
        _into.Children.Add(both);
    }

    private void Choice<T>(string label, IReadOnlyList<(string Name, T Value)> options, T current, Action<T> choose)
    {
        var combo = new ComboBox
        {
            ItemsSource = options.Select(o => o.Name).ToList(),
            SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, current))),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
            MinHeight = 28, Padding = new Thickness(8, 0), VerticalContentAlignment = VerticalAlignment.Center,
            Classes = { "field" },
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && !EqualityComparer<T>.Default.Equals(options[combo.SelectedIndex].Value, current))
            {
                choose(options[combo.SelectedIndex].Value);
            }
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*"), Margin = new Thickness(0, 2) };
        row.Children.Add(Label(label));
        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);
        _into.Children.Add(row);
    }

    private void Toggle(string label, bool on, Action<bool> set)
    {
        var box = new ToggleSwitch { IsChecked = on, OnContent = null, OffContent = null, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 0 };
        box.IsCheckedChanged += (_, _) =>
        {
            if (box.IsChecked is { } value && value != on)
            {
                set(value);
            }
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 26 };
        var text = Label(label);
        text.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(text);
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        row.PointerReleased += (_, e) =>
        {
            // a tap on the name switches as well (a bigger target than the switch)
            if (!ReferenceEquals(e.Source, box) && e.Source is not Avalonia.Controls.Shapes.Shape && box.IsEnabled)
            {
                box.IsChecked = !box.IsChecked;
            }
        };
        _into.Children.Add(row);
    }

    private void ButtonRow(params (string Text, Action Action)[] buttons)
    {
        var grid = new UniformGrid { Columns = buttons.Length };
        foreach (var (text, action) in buttons)
        {
            var button = new Button
            {
                Content = text, MinHeight = 28, Margin = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "soft" },
            };
            button.Click += (_, _) => action();
            grid.Children.Add(button);
        }

        _into.Children.Add(grid);
    }

    private Button StepButton(string text) => new()
    {
        Content = text, FontSize = 16, Width = 30, MinHeight = 28, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center, Classes = { "soft" },
    };

    private TextBlock Label(string text) => new()
    {
        Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("ForegroundTextBrush"),
    };

    private static IBrush Brush(string key) => AppTheme.Brush(key);
}
