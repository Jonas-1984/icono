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
    private const string UpIcon = "M4,13 L10,7 L16,13";
    private const string DownIcon = "M4,7 L10,13 L16,7";

    /// <summary>A removal that takes objects with it asks for a second tap: what is waiting for it.</summary>
    private object? _confirm;

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
        _content.Children.Add(pager);

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
            _content.Children.Add(LayerRow(context, layer, document.Layers.IndexOf(layer), document.Layers.Count));
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
                Text = $"{layer.Name}  ({layer.Shapes.Count})", FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush(layer.Visible ? "ForegroundTextBrush" : "MutedTextBrush"),
            },
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 4),
            MinHeight = 32,
        };
        name.Click += (_, _) =>
        {
            _confirm = null;
            context.SelectedLayer = layer;
            Rebuild();
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,30,30,26,26") };
        row.Children.Add(name);
        AddAt(row, 1, IconButton(layer.Visible ? EyeIcon : EyeOffIcon, layer.Visible ? "Hide" : "Show",
            () => context.SetLayerVisible(layer, !layer.Visible)));
        AddAt(row, 2, IconButton(layer.Locked ? LockIcon : UnlockIcon, layer.Locked ? "Unlock" : "Lock",
            () => context.SetLayerLocked(layer, !layer.Locked)));
        AddAt(row, 3, IconButton(UpIcon, "To the front", () => context.MoveLayer(layer, -1), enabled: index > 0));
        AddAt(row, 4, IconButton(DownIcon, "To the back", () => context.MoveLayer(layer, 1), enabled: index < count - 1));

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
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 15,
            Height = 15,
            Stretch = Stretch.Uniform,
            RenderTransform = rotate == 0 ? null : new RotateTransform(rotate),
        };
        var button = new Button
        {
            Classes = { "icon" },
            Content = path,
            IsEnabled = enabled,
            Opacity = enabled ? 1 : 0.35,
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            MinHeight = 32,
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
