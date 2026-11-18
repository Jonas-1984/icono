using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>Bringing a picture or an SVG into the drawing - shared by the Windows program and the tablet app: an SVG becomes shapes (one per element,
/// keeping their own colours; a stroke-style icon becomes an outline), a picture (PNG, JPEG, WebP ...) becomes a <see cref="ShapeKind.RasterImage"/>.
/// Either lands in the middle of the current page, in the selected layer, selected, as one undo step.</summary>
public static class ImageImport
{
    /// <summary>The biggest file that is read (SVG: <see cref="SvgImporter.MaxFileBytes"/>).</summary>
    public const long MaxPictureBytes = 64L * 1024 * 1024;

    /// <summary>A picture longer than this (pixels) is made smaller when it is imported, so a photo does not make the drawing file huge.</summary>
    public const int MaxPictureSide = 2048;

    private const double SvgPlaceSize = 80;
    private const double PicturePlaceSize = 160;

    /// <summary>Makes shapes of the SVG code (or bare path data) and puts them into the drawing. Null with <paramref name="error"/> set when the code
    /// holds no usable path (or the drawing has no layer).</summary>
    public static IReadOnlyList<Shape>? ImportSvg(EditorContext context, string svgMarkup, out string? error)
    {
        error = null;
        var layer = context.SelectedLayer ?? context.Document.Layers.FirstOrDefault();
        var elements = SvgImporter.ExtractElements(svgMarkup);
        if (layer is null || elements.Count == 0)
        {
            error = "No valid SVG path was found in the pasted code.";
            return null;
        }

        // the placement box has the source's own proportions (the larger side is SvgPlaceSize): the shapes are stretched to fill it
        var placeWidth = SvgPlaceSize;
        var placeHeight = SvgPlaceSize;
        if (SvgImporter.MeasureBounds(elements.Select(el => el.PathData).ToList()) is { } measured)
        {
            var scale = SvgPlaceSize / Math.Max(measured.Width, measured.Height);
            placeWidth = measured.Width * scale;
            placeHeight = measured.Height * scale;
        }

        var (placeX, placeY) = CentreOnPage(context, placeWidth, placeHeight);
        var strokeOnly = SvgImporter.IsStrokeStyleSvg(svgMarkup);
        var imported = SvgImporter.BuildShapes(
            elements, context.CurrentFillColor, context.CurrentFillColor, strokeOnly ? 2 : 0, placeX, placeY, placeWidth, placeHeight, strokeOnly);
        if (imported.Count == 0)
        {
            error = "The SVG code does not contain a valid path.";
            return null;
        }

        Insert(context, layer, imported);
        return imported;
    }

    /// <summary>Puts a picture (any format Skia reads) into the drawing as an image shape; a big one is made smaller first. Null with
    /// <paramref name="error"/> set when the bytes are no picture.</summary>
    public static Shape? ImportPicture(EditorContext context, byte[] data, out string? error)
    {
        error = null;
        if (data.Length == 0 || data.Length > MaxPictureBytes)
        {
            error = data.Length == 0 ? "The file could not be read as an image." : "The picture is too big.";
            return null;
        }

        // Skia throws (instead of returning null) for bytes it has no codec for: look for the codec first
        // (an .ico file is a container of several pictures: its largest one is taken)
        using var source = SKData.CreateCopy(data);
        using var codec = IcoReader.IsIco(data) ? null : SKCodec.Create(source);
        using var decoded = IcoReader.IsIco(data) ? IcoReader.LargestFrame(data) : codec is null ? null : SKBitmap.Decode(codec);
        if (decoded is null || decoded.Width < 1 || decoded.Height < 1)
        {
            error = "The file could not be read as an image.";
            return null;
        }

        var png = ToPng(decoded);
        return png is null ? Fail("The file could not be read as an image.", out error) : InsertPicture(context, png, decoded.Width, decoded.Height, out error);
    }

    /// <summary>The longest side (pixels) of a picture used as the background of the page.</summary>
    public const int MaxBackgroundSide = 3000;

    /// <summary>A picture (any format Skia reads, or an .ico) as the PNG bytes for the page background, at most 3000 px on its long side. Null with
    /// <paramref name="error"/> set when the bytes are no picture.</summary>
    public static byte[]? ToBackgroundPng(byte[] data, out string? error)
    {
        error = null;
        if (data.Length == 0 || data.Length > MaxPictureBytes)
        {
            error = data.Length == 0 ? "The file could not be read as an image." : "The picture is too big.";
            return null;
        }

        using var source = SKData.CreateCopy(data);
        using var codec = IcoReader.IsIco(data) ? null : SKCodec.Create(source);
        using var decoded = IcoReader.IsIco(data) ? IcoReader.LargestFrame(data) : codec is null ? null : SKBitmap.Decode(codec);
        var png = decoded is null || decoded.Width < 1 || decoded.Height < 1 ? null : ToPng(decoded, MaxBackgroundSide);
        if (png is null)
        {
            error = "The file could not be read as an image.";
        }

        return png;
    }

    /// <summary>Puts picture bytes (PNG) of the given pixel size into the drawing as an image shape, at most 160 px on its long side.</summary>
    public static Shape? InsertPicture(EditorContext context, byte[] png, int pixelWidth, int pixelHeight, out string? error)
    {
        error = null;
        var layer = context.SelectedLayer ?? context.Document.Layers.FirstOrDefault();
        if (layer is null)
        {
            error = "There is no layer to put the picture on.";
            return null;
        }

        var scale = Math.Min(1.0, PicturePlaceSize / Math.Max(pixelWidth, pixelHeight));
        var width = Math.Max(1, pixelWidth * scale);
        var height = Math.Max(1, pixelHeight * scale);
        var (x, y) = CentreOnPage(context, width, height);
        var shape = new Shape { Kind = ShapeKind.RasterImage, X = x, Y = y, Width = width, Height = height, ImageBytes = png };
        Insert(context, layer, [shape]);
        return shape;
    }

    private static Shape? Fail(string message, out string? error)
    {
        error = message;
        return null;
    }

    /// <summary>The picture as PNG bytes, made smaller when its long side is over <see cref="MaxPictureSide"/>.</summary>
    private static byte[]? ToPng(SKBitmap source, int maxSide = MaxPictureSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.Width, source.Height));
        using var image = scale < 1
            ? ResizedImage(source, Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)))
            : SKImage.FromBitmap(source);
        using var encoded = image?.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }

    private static SKImage? ResizedImage(SKBitmap source, int width, int height)
    {
        using var smaller = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return smaller is null ? null : SKImage.FromBitmap(smaller);
    }

    private static (double X, double Y) CentreOnPage(EditorContext context, double width, double height) =>
        (Math.Max(0, (context.Document.Width - width) / 2),
         context.Document.PageTop(context.CurrentPage) + Math.Max(0, (context.Document.Height - height) / 2));

    /// <summary>Puts the shapes on top of the layer, selects them, and records one undo step.</summary>
    private static void Insert(EditorContext context, Layer layer, IReadOnlyList<Shape> shapes)
    {
        var actions = new List<UndoableAction>();
        foreach (var shape in shapes)
        {
            layer.Shapes.Insert(0, shape);
            actions.Add(UndoableAction.ForInsert(layer.Shapes, shape, 0));
        }

        context.UndoManager.Push(UndoableAction.Combine(actions));
        context.SelectedShapes.Clear();
        foreach (var shape in shapes)
        {
            context.SelectedShapes.Add(shape);
        }

        context.SelectedLayer = layer;
        context.RequestRedraw();
    }
}
