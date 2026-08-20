using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Icono.App.State;
using Icono.App.State.Undo;
using Icono.Core;
using Icono.IO;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>
/// A growable library of saved shape groups — the seed the user can keep adding to later, backed
/// by <see cref="IconLibraryStore"/> (Icono.IO's first real content). Click a thumbnail to insert
/// a copy of its shapes into the current layer; "+ Save Selection" adds a new entry from the
/// current canvas selection.
/// </summary>
public partial class IconLibraryView : UserControl
{
    private readonly IconLibraryStore _store = new();
    private EditorContext? _context;

    public EditorContext? Context
    {
        get => _context;
        set
        {
            _context = value;
            RefreshLibrary();
        }
    }

    public IconLibraryView()
    {
        InitializeComponent();
    }

    /// <summary>Same reusable collapse/expand pattern as PropertiesPanelView/ToolPanelView.</summary>
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

    private void RefreshLibrary()
    {
        var entries = _store.LoadAll();
        var items = entries.Select(entry => new IconLibraryListItem(entry, RenderThumbnail(entry, 52))).ToList();

        LibraryItemsControl.ItemsSource = items;
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveSelectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || _context.SelectedShapes.Count == 0)
        {
            return;
        }

        var name = $"Icon {DateTime.Now:HH:mm:ss}";
        var entry = new IconLibraryEntry
        {
            Name = name,
            Shapes = _context.SelectedShapes.Select(CloneShape).ToList(),
        };

        _store.Save(entry);
        RefreshLibrary();
    }

    /// <summary>Opens <see cref="ImportSvgDialog"/>, extracts every path's "d" data from what was
    /// pasted, builds one CombinedPath shape per path (all sharing one placement, so a multi-path
    /// icon stays aligned — see <see cref="SvgImporter.BuildShapes"/>), inserts them onto the
    /// canvas, and also saves them as a reusable "MY ICONS" entry.</summary>
    private void ImportSvgButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        var dialog = new ImportSvgDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
        var pathData = SvgImporter.ExtractPathData(dialog.SvgCode);

        if (layer is null || pathData.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Kein gültiger SVG-Pfad im eingefügten Code gefunden.",
                "SVG importieren", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        const double placeSize = 80;
        var placeX = Math.Max(0, (_context.Document.Width - placeSize) / 2);
        var placeY = Math.Max(0, (_context.Document.Height - placeSize) / 2);

        var imported = SvgImporter.BuildShapes(
            pathData, _context.CurrentFillColor, _context.CurrentFillColor, 0,
            placeX, placeY, placeSize, placeSize);

        if (imported.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Der SVG-Code enthält keinen gültigen Pfad.",
                "SVG importieren", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var undoActions = new List<UndoableAction>();
        foreach (var shape in imported)
        {
            layer.Shapes.Insert(0, shape);
            undoActions.Add(UndoableAction.ForInsert(layer.Shapes, shape, 0));
        }

        _context.UndoManager.Push(UndoableAction.Combine(undoActions));

        _context.SelectedShapes.Clear();
        foreach (var shape in imported)
        {
            _context.SelectedShapes.Add(shape);
        }

        _context.SelectedLayer = layer;
        _context.RequestRedraw();

        var entry = new IconLibraryEntry
        {
            Name = $"SVG {DateTime.Now:HH:mm:ss}",
            Shapes = imported.Select(CloneShape).ToList(),
        };
        _store.Save(entry);
        RefreshLibrary();
    }

    private void LibraryItem_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_context is null || sender is not FrameworkElement { Tag: IconLibraryListItem item })
        {
            return;
        }

        var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
        if (layer is null)
        {
            return;
        }

        var inserted = new List<Shape>();
        foreach (var sourceShape in item.Entry.Shapes)
        {
            var shape = CloneShape(sourceShape);
            layer.Shapes.Insert(0, shape);
            inserted.Add(shape);
        }

        if (inserted.Count > 0)
        {
            // Every shape above was inserted at index 0 (front-most) at the moment of its own
            // insertion, so that's the correct redo-index for each — not its position in `inserted`.
            var undoActions = inserted.Select(s => UndoableAction.ForInsert(layer.Shapes, s, 0)).ToList();
            _context.UndoManager.Push(UndoableAction.Combine(undoActions));

            _context.SelectedShapes.Clear();
            foreach (var shape in inserted)
            {
                _context.SelectedShapes.Add(shape);
            }
        }

        _context.SelectedLayer = layer;
        _context.RequestRedraw();
    }

    private static Shape CloneShape(Shape source)
    {
        var clone = new Shape { Kind = source.Kind };
        ShapeSnapshot.Capture(source).ApplyTo(clone);
        return clone;
    }

    private static BitmapImage RenderThumbnail(IconLibraryEntry entry, int size)
    {
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        CheckerboardRenderer.Render(canvas, size, size);

        if (entry.Shapes.Count > 0)
        {
            var minX = entry.Shapes.Min(s => s.X);
            var minY = entry.Shapes.Min(s => s.Y);
            var maxX = entry.Shapes.Max(s => s.X + s.Width);
            var maxY = entry.Shapes.Max(s => s.Y + s.Height);
            var w = Math.Max(1, maxX - minX);
            var h = Math.Max(1, maxY - minY);
            var scale = (float)(size * 0.8 / Math.Max(w, h));
            var offsetX = (size - w * scale) / 2 - minX * scale;
            var offsetY = (size - h * scale) / 2 - minY * scale;

            canvas.Save();
            canvas.Translate((float)offsetX, (float)offsetY);
            canvas.Scale(scale);

            foreach (var shape in entry.Shapes)
            {
                ShapeRenderer.Draw(canvas, shape);
            }

            canvas.Restore();
        }

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
}
