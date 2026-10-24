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
        var items = entries
            .Select(e => new IconLibraryListItem(e.Entry, RenderThumbnail(e.Entry, 52), e.FilePath))
            .ToList();

        LibraryItemsControl.ItemsSource = items;
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DeleteLibraryItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: IconLibraryListItem item })
        {
            return;
        }

        _store.Delete(item.FilePath);
        RefreshLibrary();
    }

    /// <summary>Saves the current selection as a reusable "MY ICONS" entry — expanded to every
    /// OTHER shape sharing a GroupId with anything selected, not just whatever's literally in
    /// SelectedShapes. Needed because a click can only ever hit the TOPMOST shape at a given
    /// point: for two shapes placed exactly on top of each other (e.g. a colored base + a faded
    /// highlight layer), only that topmost one may have actually made it into the selection even
    /// though the whole group visually reads as one icon — saving must still capture all of it.</summary>
    private void SaveSelectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || _context.SelectedShapes.Count == 0)
        {
            return;
        }

        var groupIds = _context.SelectedShapes
            .Select(s => s.GroupId)
            .Where(id => id is not null)
            .ToHashSet();

        var shapesToSave = _context.SelectedShapes
            .Concat(_context.Document.Layers.SelectMany(l => l.Shapes)
                .Where(s => s.GroupId is not null && groupIds.Contains(s.GroupId)))
            .Distinct()
            .ToList();

        var name = $"Icon {DateTime.Now:HH:mm:ss}";
        var entry = new IconLibraryEntry
        {
            Name = name,
            Shapes = shapesToSave.Select(CloneShape).ToList(),
        };

        _store.Save(entry);
        RefreshLibrary();
    }

    /// <summary>Opens <see cref="ImportSvgDialog"/>, extracts every path/circle/ellipse/rect's
    /// equivalent "d" data from what was pasted, builds one CombinedPath shape per element (all
    /// sharing one placement, so a multi-element icon stays aligned — see
    /// <see cref="SvgImporter.BuildShapes"/>), inserts them onto the canvas, and also saves them as
    /// a reusable "MY ICONS" entry. A stroke-style source (fill="none" — the Lucide/Feather/Tabler
    /// convention almost every hand-pasted icon uses) imports as an outline instead of a solid
    /// fill, matching how the source actually looks instead of a garbled filled blob.</summary>
    /// <summary>Opens the paste-SVG-code dialog (also reachable from Insert > SVG Code).</summary>
    public void ImportSvgFromDialog() => ImportSvgButton_OnClick(this, new RoutedEventArgs());

    /// <summary>Opens the picture/SVG file chooser (also reachable from Insert > Image).</summary>
    public void ImportImageFromDialog() => ImportImageButton_OnClick(this, new RoutedEventArgs());

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

        ImportSvgMarkupAsShapes(dialog.SvgCode);
    }

    /// <summary>Extracts every path/circle/ellipse/rect element from <paramref name="svgMarkup"/>
    /// (each keeping its OWN fill color if it declared one — see <see cref="SvgImporter.ExtractElements"/>),
    /// builds one CombinedPath shape per element (all sharing one placement, so a multi-element
    /// icon stays aligned — see <see cref="SvgImporter.BuildShapes(IReadOnlyList{SvgImporter.SvgElement},ArgbColor,ArgbColor,double,double,double,double,double,bool)"/>),
    /// inserts them onto the canvas, and also saves them as a reusable "MY ICONS" entry. A
    /// stroke-style source (fill="none" — the Lucide/Feather/Tabler convention almost every icon
    /// library uses) imports as an outline instead of a solid fill, matching how the source
    /// actually looks instead of a garbled filled blob; a multi-color source (e.g. a two-tone logo)
    /// keeps each piece's own color instead of flattening everything to the app's current fill
    /// color. Shared by both the paste-code dialog (<see cref="ImportSvgButton_OnClick"/>) and
    /// picking an .svg file (<see cref="ImportImageButton_OnClick"/>) — same markup, same import
    /// rules either way.</summary>
    private bool ImportSvgMarkupAsShapes(string svgMarkup)
    {
        if (_context is null)
        {
            return false;
        }

        var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
        var elements = SvgImporter.ExtractElements(svgMarkup);

        if (layer is null || elements.Count == 0)
        {
            ThemedMessageBox.Show(Window.GetWindow(this), "No valid SVG path was found in the pasted code.",
                "Import SVG", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // Size the placement box to the source's OWN aspect ratio (capped so the larger side is
        // placeSize) instead of always forcing a fixed 80x80 square — BuildShapes stretches its
        // normalized geometry non-uniformly to fill whatever box it's given, so a square box used
        // to squash/stretch every non-square icon into a distorted square on import.
        const double placeSize = 80;
        var placeWidth = placeSize;
        var placeHeight = placeSize;
        if (SvgImporter.MeasureBounds(elements.Select(el => el.PathData).ToList()) is { } measured)
        {
            var scale = placeSize / Math.Max(measured.Width, measured.Height);
            placeWidth = measured.Width * scale;
            placeHeight = measured.Height * scale;
        }

        var placeX = Math.Max(0, (_context.Document.Width - placeWidth) / 2);
        var placeY = Math.Max(0, (_context.Document.Height - placeHeight) / 2);

        var strokeOnly = SvgImporter.IsStrokeStyleSvg(svgMarkup);
        var borderThickness = strokeOnly ? 2 : 0;

        var imported = SvgImporter.BuildShapes(
            elements, _context.CurrentFillColor, _context.CurrentFillColor, borderThickness,
            placeX, placeY, placeWidth, placeHeight, strokeOnly);

        if (imported.Count == 0)
        {
            ThemedMessageBox.Show(Window.GetWindow(this), "The SVG code does not contain a valid path.",
                "Import SVG", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
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
        return true;
    }

    /// <summary>Opens a native file picker for .png/.ico/.svg. An .svg file is read as text and
    /// handed to the same <see cref="ImportSvgMarkupAsShapes"/> path the paste-code dialog uses —
    /// vector shapes, not a rasterized image. A .png/.ico decodes into PNG bytes (an .ico's largest
    /// frame is picked and re-encoded — SkiaSharp can't decode the .ico container itself, so this
    /// normalizes to PNG once at import time so <c>Icono.Rendering.ShapeRenderer</c> only ever has
    /// to handle one raster format), inserted as a <see cref="ShapeKind.RasterImage"/> shape sized
    /// to the source pixel dimensions (capped so huge source images don't dwarf the canvas), and
    /// also saved as a reusable "MY ICONS" entry.</summary>
    private void ImportImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.png;*.ico;*.svg)|*.png;*.ico;*.svg",
            Title = "Import Image",
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        if (Path.GetExtension(dialog.FileName).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            string svgMarkup;
            try
            {
                svgMarkup = File.ReadAllText(dialog.FileName);
            }
            catch (Exception)
            {
                ThemedMessageBox.Show(Window.GetWindow(this), "The file could not be read.",
                    "Import image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ImportSvgMarkupAsShapes(svgMarkup);
            return;
        }

        byte[] pngBytes;
        int pixelWidth;
        int pixelHeight;

        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();

            var bitmapSource = extension == ".ico"
                ? new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                    .Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First()
                : BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];

            pixelWidth = bitmapSource.PixelWidth;
            pixelHeight = bitmapSource.PixelHeight;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using var outStream = new MemoryStream();
            encoder.Save(outStream);
            pngBytes = outStream.ToArray();
        }
        catch (Exception)
        {
            ThemedMessageBox.Show(Window.GetWindow(this), "The file could not be read as an image.",
                "Import image", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
        if (layer is null)
        {
            return;
        }

        const double maxPlaceSize = 160;
        var scale = Math.Min(1.0, maxPlaceSize / Math.Max(pixelWidth, pixelHeight));
        var placeWidth = Math.Max(1, pixelWidth * scale);
        var placeHeight = Math.Max(1, pixelHeight * scale);
        var placeX = Math.Max(0, (_context.Document.Width - placeWidth) / 2);
        var placeY = Math.Max(0, (_context.Document.Height - placeHeight) / 2);

        var shape = new Shape
        {
            Kind = ShapeKind.RasterImage,
            X = placeX,
            Y = placeY,
            Width = placeWidth,
            Height = placeHeight,
            ImageBytes = pngBytes,
        };

        layer.Shapes.Insert(0, shape);
        _context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));

        _context.SelectedShapes.Clear();
        _context.SelectedShapes.Add(shape);
        _context.SelectedLayer = layer;
        _context.RequestRedraw();

        var entry = new IconLibraryEntry
        {
            Name = $"Image {DateTime.Now:HH:mm:ss}",
            Shapes = [CloneShape(shape)],
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
