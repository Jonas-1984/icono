using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Icono.Core;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>The "Pages &amp; layers" tab: go to a page, add or remove pages; choose the active layer (new objects go there), show / hide, lock,
/// order, rename and delete layers, and move the selection to a layer. The work is done by <see cref="EditorContext"/> (shared with Windows).</summary>
public sealed partial class PropertiesPanel
{
    private const string EyeIcon =
        "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12";

    private const string EyeOffIcon =
        "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49 M14.084 14.158a3 3 0 0 1-4.242-4.242 " +
        "M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143 M2,2 l20,20";

    private const string LockIcon = "M5,11 H19 A2,2 0 0 1 21,13 V20 A2,2 0 0 1 19,22 H5 A2,2 0 0 1 3,20 V13 A2,2 0 0 1 5,11 Z M7 11V7a5 5 0 0 1 10 0v4";
    private const string UnlockIcon = "M5,11 H19 A2,2 0 0 1 21,13 V20 A2,2 0 0 1 19,22 H5 A2,2 0 0 1 3,20 V13 A2,2 0 0 1 5,11 Z M7 11V7a5 5 0 0 1 9.9 -1";
    private const string UpIcon = "M5,12.5 L10,7.5 L15,12.5";
    private const string DownIcon = "M5,7.5 L10,12.5 L15,7.5";

    /// <summary>A removal that takes objects with it asks for a second tap: what is waiting for it.</summary>
    private object? _confirm;

    /// <summary>The layers whose objects are listed (folded open with the arrow in front of the name).</summary>
    private readonly HashSet<Layer> _openLayers = [];

    /// <summary>Raised by "Rename": the window asks for the new name.</summary>
    public event EventHandler<Layer>? RenameLayerRequested;

    private void BuildPagesAndLayers(EditorContext context)
    {
        var document = context.Document;
        Title("Pages & layers");

        // ----- pages
        Heading("PAGES");
        var page = context.CurrentPage;
        var pager = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,40"), Margin = new Thickness(0, 2) };
        var previous = IconButton(DownIcon, "Previous page", () => context.ShowPage(page - 1), rotate: 90, enabled: page > 0);
        var next = IconButton(DownIcon, "Next page", () => context.ShowPage(page + 1), rotate: -90, enabled: page < document.PageCount - 1);
        var where = new TextBlock
        {
            Text = $"Page {page + 1} of {document.PageCount}", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("ForegroundTextBrush"),
        };
        Grid.SetColumn(where, 1);
        Grid.SetColumn(next, 2);
        pager.Children.Add(previous);
        pager.Children.Add(where);
        pager.Children.Add(next);
        _into.Children.Add(pager);

        var onLastPage = context.ShapesOnLastPage().Count;
        var confirmPage = Equals(_confirm, "page");
        ButtonRow(
            ("Add page", () =>
            {
                _confirm = null;
                context.AddPage();
            }),
            (confirmPage ? "Remove it" : "Remove last", () =>
            {
                if (document.PageCount <= 1)
                {
                    return;
                }

                if (onLastPage > 0 && !confirmPage)
                {
                    _confirm = "page";
                    Rebuild();
                    return;
                }

                _confirm = null;
                context.RemoveLastPage();
            }));
        if (confirmPage)
        {
            Note($"Page {document.PageCount} holds {onLastPage} object(s). Tap \"Remove it\" again to remove the page with them (Undo brings it back).");
        }

        // ----- layers
        Heading("LAYERS");
        Note("New objects go on the active layer (marked blue). The top layer is drawn in front.");
        foreach (var layer in document.Layers)
        {
            _into.Children.Add(LayerRow(context, layer, document.Layers.IndexOf(layer), document.Layers.Count));
            if (_openLayers.Contains(layer))
            {
                _into.Children.Add(ShapeList(context, layer));
            }
        }

        var active = context.SelectedLayer ?? document.Layers.FirstOrDefault();
        if (active is null)
        {
            return;
        }

        var confirmDelete = ReferenceEquals(_confirm, active);
        ButtonRow(
            ("Add layer", () =>
            {
                _confirm = null;
                context.AddLayer();
            }),
            ("Rename", () => RenameLayerRequested?.Invoke(this, active)),
            (confirmDelete ? "Delete it" : "Delete", () =>
            {
                if (active.Shapes.Count > 0 && !confirmDelete && document.Layers.Count > 1)
                {
                    _confirm = active;
                    Rebuild();
                    return;
                }

                _confirm = null;
                context.DeleteLayer(active);
            }));
        if (confirmDelete)
        {
            Note($"\"{active.Name}\" holds {active.Shapes.Count} object(s). Tap \"Delete it\" again to delete the layer with them (Undo brings it back).");
        }

        var movable = context.SelectedShapes.Count(s => !ReferenceEquals(LayerOf(context, s), active));
        if (movable > 0)
        {
            ButtonRow(($"Move {movable} selected object(s) to \"{active.Name}\"", () => context.MoveSelectionToLayer(active)));
        }
    }

    private static Layer? LayerOf(EditorContext context, Shape shape) => context.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));

    /// <summary>One layer: its name (tap: make it the active one) with the number of objects, then show / hide, lock, front, back.</summary>
    private Control LayerRow(EditorContext context, Layer layer, int index, int count)
    {
        var isActive = ReferenceEquals(layer, context.SelectedLayer ?? context.Document.Layers.FirstOrDefault());
        var name = new Button
        {
            Content = new TextBlock
            {
                Text = $"{layer.Name}   {layer.Shapes.Count}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(layer.Visible ? "ForegroundTextBrush" : "MutedTextBrush"),
            },
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 0),
            MinHeight = 24,
            Height = 24,
        };
        name.Click += (_, _) =>
        {
            _confirm = null;
            context.SelectedLayer = layer;
            Rebuild();
        };

        var open = _openLayers.Contains(layer);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,26,26,26,26"), MinHeight = 26 };
        AddAt(row, 0, IconButton(open ? DownIcon : "M7,4 L13,10 L7,16", open ? "Hide the objects of the layer" : "Show the objects of the layer", () =>
        {
            if (!_openLayers.Remove(layer))
            {
                _openLayers.Add(layer);
            }

            Rebuild();
        }));
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        AddAt(row, 2, IconButton(layer.Visible ? EyeIcon : EyeOffIcon, layer.Visible ? "Hide" : "Show",
            () => context.SetLayerVisible(layer, !layer.Visible)));
        AddAt(row, 3, IconButton(layer.Locked ? LockIcon : UnlockIcon, layer.Locked ? "Unlock" : "Lock",
            () => context.SetLayerLocked(layer, !layer.Locked)));
        AddAt(row, 4, IconButton(UpIcon, "To the front", () => context.MoveLayer(layer, -1), enabled: index > 0));
        AddAt(row, 5, IconButton(DownIcon, "To the back", () => context.MoveLayer(layer, 1), enabled: index < count - 1));

        return new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 1),
            Background = isActive ? new SolidColorBrush(Color.FromArgb(0x33, 0x5A, 0xB0, 0xFF)) : Brushes.Transparent,
            BorderBrush = isActive ? Brush("AccentBrush") : Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>The objects of one layer, the front-most first: tap one to select it, the arrows move it forward or back within the layer (as in the
    /// layers list of the Windows program).</summary>
    private Control ShapeList(EditorContext context, Layer layer)
    {
        var list = new StackPanel { Spacing = 1, Margin = new Thickness(18, 0, 0, 6) };
        if (layer.Shapes.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = "No objects on this layer.", FontSize = 11.5, Foreground = Brush("MutedTextBrush"), Margin = new Thickness(6, 4) });
            return list;
        }

        var count = layer.Shapes.Count;
        for (var i = 0; i < count; i++)
        {
            var shape = layer.Shapes[i];
            var index = i;
            var selected = context.SelectedShapes.Contains(shape);
            var label = !string.IsNullOrWhiteSpace(shape.Name)
                ? shape.Name
                : shape.Kind == ShapeKind.Text && !string.IsNullOrWhiteSpace(shape.TextContent)
                    ? $"Text \"{(shape.TextContent!.Length > 18 ? shape.TextContent[..18] + "…" : shape.TextContent)}\""
                    : $"{Words(shape.Kind.ToString())} {i + 1}";
            var dot = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(shape.FillColor.R, shape.FillColor.G, shape.FillColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(shape.BorderColor.R, shape.BorderColor.G, shape.BorderColor.B)), BorderThickness = new Thickness(1.5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var text = new TextBlock
            {
                Text = label + (shape.Locked ? "  (locked)" : string.Empty), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush(shape.Locked || !layer.Visible ? "MutedTextBrush" : "ForegroundTextBrush"), VerticalAlignment = VerticalAlignment.Center,
            };
            var name = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text } }, Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 0), MinHeight = 26,
            };
            name.Click += (_, _) =>
            {
                context.SelectedLayer = layer;
                context.SelectedShape = shape;
                context.RequestRedraw();
            };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,30,30") };
            row.Children.Add(name);
            AddAt(row, 1, IconButton(UpIcon, "Forward", () => context.MoveShapeInLayer(layer, shape, -1), enabled: index > 0 && !layer.Locked && !shape.Locked));
            AddAt(row, 2, IconButton(DownIcon, "Back", () => context.MoveShapeInLayer(layer, shape, 1), enabled: index < count - 1 && !layer.Locked && !shape.Locked));
            list.Children.Add(new Border
            {
                Child = row, CornerRadius = new CornerRadius(6),
                Background = selected ? new SolidColorBrush(Color.FromArgb(0x33, 0x5A, 0xB0, 0xFF)) : Brushes.Transparent,
            });
        }

        return list;
    }

    private static void AddAt(Grid grid, int column, Control control)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private Button IconButton(string data, string tip, Action action, double rotate = 0, bool enabled = true)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = Brush(enabled ? "ForegroundTextBrush" : "MutedTextBrush"),
            StrokeThickness = 1.9,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 12,
            Height = 12,
            Stretch = Stretch.Uniform,
            RenderTransform = rotate == 0 ? null : new RotateTransform(rotate),
        };
        var button = new Button
        {
            Classes = { "icon" },
            Content = path,
            IsEnabled = enabled,
            Opacity = enabled ? 1 : 0.35,
            Background = new SolidColorBrush(Color.FromArgb(enabled ? (byte)0x1C : (byte)0x08, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(1, 0),
            Padding = new Thickness(0),
            MinHeight = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) =>
        {
            _confirm = null;
            action();
        };
        return button;
    }
}
