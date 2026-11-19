using System.IO.Compression;
using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Renders a <see cref="Document"/> to files: PNG at any size, SVG, PDF at the real paper size, and multi-size Windows .ico files.</summary>
public static class DocumentExporter
{
    /// <summary>Standard sizes of a Windows icon, smallest first.</summary>
    public static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>Renders the page so that its LONGER side is <paramref name="longSide"/> px (the aspect ratio is kept).
    /// With <paramref name="includeBackground"/> the page background (colour / blur / picture) is part of the image,
    /// otherwise the result is transparent. <paramref name="page"/> picks one page of a project with several (0 = the first);
    /// null renders all pages below each other.</summary>
    public static byte[] RenderPng(Document document, int longSide, bool includeBackground, int? page = null, bool onlyDrawing = false)
    {
        SKRect? crop = onlyDrawing ? DrawnArea(document, page) : null;
        if (crop is not null)
        {
            includeBackground = false; // only the drawing: nothing of the page, the rest is transparent
        }

        var (extentWidth, extentHeight) = crop is { } area ? ((double)area.Width, (double)area.Height) : Extent(document, page);
        var scale = longSide / Math.Max(extentWidth, extentHeight);
        var width = Math.Max(1, (int)Math.Round(extentWidth * scale));
        var height = Math.Max(1, (int)Math.Round(extentHeight * scale));
        return Encode(RenderBitmap(document, width, height, includeBackground, page, crop), SKEncodedImageFormat.Png, 100);
    }

    /// <summary>Sizes (longer side, px) of the pictures of a PNG set.</summary>
    public static readonly int[] PngSetSizes = [16, 32, 64, 128, 256, 512];

    /// <summary>A zip file with the pictures of a PNG set (<see cref="PngSetSizes"/>) named "{baseName}_{size}.png" - for a project with several pages and
    /// <paramref name="page"/> null "{baseName}_page{n}_{size}.png" for every page. The Windows program writes the same pictures into a folder.</summary>
    public static byte[] RenderPngSetZip(Document document, string baseName, bool includeBackground, int? page = null, bool onlyDrawing = false) =>
        RenderPngSetZip(document, baseName, includeBackground, page is { } one ? [one] : Enumerable.Range(0, document.PageCount).ToList(), onlyDrawing);

    /// <summary>The same for the pages in <paramref name="pages"/> (0-based); with more than one page the names carry the page number.</summary>
    public static byte[] RenderPngSetZip(Document document, string baseName, bool includeBackground, IReadOnlyList<int> pages, bool onlyDrawing = false)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var index in pages.Distinct().Where(p => p >= 0 && p < document.PageCount))
            {
                var name = pages.Count > 1 ? $"{baseName}_page{index + 1}" : baseName;
                foreach (var size in PngSetSizes)
                {
                    var entry = zip.CreateEntry($"{name}_{size}.png", CompressionLevel.Optimal);
                    using var output = entry.Open();
                    var png = RenderPng(document, size, includeBackground, index, onlyDrawing);
                    output.Write(png, 0, png.Length);
                }
            }
        }

        return stream.ToArray();
    }

    /// <summary>The drawing as an SVG file (vector graphic, text as outlines, sizes in px at 96 per inch): one page, or with <paramref name="page"/> null all
    /// pages below each other. With <paramref name="includeBackground"/> the page background is part of the file.</summary>
    public static byte[] RenderSvg(Document document, bool includeBackground, int? page = null, bool onlyDrawing = false)
    {
        SKRect? crop = onlyDrawing ? DrawnArea(document, page) : null;
        if (crop is not null)
        {
            includeBackground = false;
        }

        var (width, height) = crop is { } area ? ((double)area.Width, (double)area.Height) : Extent(document, page);
        using var stream = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, (float)width, (float)height), stream))
        {
            var top = page is { } index ? document.PageTop(index) : 0;
            if (crop is { } drawn)
            {
                // only the part of the page where something is drawn
                canvas.Translate(-drawn.Left, -(top + drawn.Top));
                canvas.ClipRect(SKRect.Create(drawn.Left, top + drawn.Top, drawn.Width, drawn.Height));
            }
            else if (page is not null)
            {
                canvas.Translate(0, -top);
                // only this page is written, not the drawing of the other pages
                canvas.ClipRect(SKRect.Create(0, top, document.Width, document.Height));
            }

            DocumentRenderer.Render(canvas, document, document.Width, document.Height, null, forExport: true, includeBackground);
        }

        return stream.ToArray();
    }

    /// <summary>The drawing as a PDF: one PDF page per page of the drawing (<paramref name="pages"/>, or all), each exactly as big as the paper
    /// (96 drawing units = 1 inch = 72 pt, so an A4 page is 210 x 297 mm) and in vectors - printed at 100 % it keeps the drawing scale.</summary>
    public static byte[] RenderPdf(Document document, bool includeBackground, IReadOnlyList<int>? pages = null)
    {
        const float PointsPerUnit = 72f / 96f;
        var chosen = (pages is { Count: > 0 } ? pages : Enumerable.Range(0, document.PageCount))
            .Where(p => p >= 0 && p < document.PageCount)
            .Distinct()
            .ToList();
        if (chosen.Count == 0)
        {
            chosen.Add(0);
        }

        using var stream = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Creator = "icono", Producer = "icono" }))
        {
            foreach (var index in chosen)
            {
                var canvas = pdf.BeginPage(document.Width * PointsPerUnit, document.Height * PointsPerUnit);
                canvas.Scale(PointsPerUnit);
                canvas.Translate(0, -document.PageTop(index));
                // only this page is written, not the drawing of the other pages
                canvas.ClipRect(SKRect.Create(0, document.PageTop(index), document.Width, document.Height));
                DocumentRenderer.Render(canvas, document, document.Width, document.Height, null, forExport: true, includeBackground);
                pdf.EndPage();
            }

            pdf.Close();
        }

        return stream.ToArray();
    }

    /// <summary>Where something is drawn: the smallest rectangle that holds everything that is visible on one page (relative to the top of that page) or, with
    /// <paramref name="page"/> null, on all pages (relative to the top of the first). Found on the picture itself - the page is rendered without its background and the
    /// pixels that are not transparent are measured - so shadows, glows, texts and dimensions are part of it. Throws when nothing is drawn.</summary>
    public static SKRect DrawnArea(Document document, int? page = null)
    {
        var (width, height) = Extent(document, page);
        var scale = Math.Min(1.0, MaxScanSide / (double)Math.Max(width, height));
        var scanWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        var scanHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        using var bitmap = RenderBitmap(document, scanWidth, scanHeight, includeBackground: false, page);

        int minX = scanWidth, minY = scanHeight, maxX = -1, maxY = -1;
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < scanHeight; y++)
        {
            var row = y * scanWidth * 4;
            for (var x = 0; x < scanWidth; x++)
            {
                if (pixels[row + x * 4 + 3] == 0)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < 0)
        {
            throw new InvalidOperationException("There is nothing drawn to export.");
        }

        // a little room round it (the pixel steps of a scaled scan, and the soft edges)
        var margin = 1 / scale + 1;
        var left = Math.Max(0, minX / scale - margin);
        var top = Math.Max(0, minY / scale - margin);
        var right = Math.Min(width, (maxX + 1) / scale + margin);
        var bottom = Math.Min(height, (maxY + 1) / scale + margin);
        return new SKRect((float)Math.Floor(left), (float)Math.Floor(top), (float)Math.Ceiling(right), (float)Math.Ceiling(bottom));
    }

    /// <summary>The longest side of the picture on which the drawn area is looked for (a bigger page is looked at in a smaller picture).</summary>
    private const int MaxScanSide = 4096;

    /// <summary>The size of what is rendered: one page, or all pages below each other.</summary>
    public static (int Width, int Height) Extent(Document document, int? page) =>
        page is null ? (document.Width, document.TotalHeight) : (document.Width, document.Height);

    /// <summary>Renders the page (or, with <paramref name="page"/> null, all pages below each other) at an exact pixel size.</summary>
    public static SKBitmap RenderBitmap(Document document, int width, int height, bool includeBackground, int? page = null, SKRect? crop = null)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var (extentWidth, extentHeight) = crop is { } area ? ((double)area.Width, (double)area.Height) : Extent(document, page);
        canvas.Scale((float)(width / extentWidth), (float)(height / extentHeight));
        if (crop is { } part)
        {
            canvas.Translate(-part.Left, -part.Top); // the part of the page (or of all pages) that is drawn
        }

        if (page is { } index)
        {
            canvas.Translate(0, -document.PageTop(index));
        }

        DocumentRenderer.Render(canvas, document, document.Width, document.Height, null, forExport: true, includeBackground);
        return bitmap;
    }

    /// <summary>Builds a .ico with one PNG frame per size. The page is fitted (centred, aspect ratio kept) into each
    /// square frame; the rest of the frame stays transparent.</summary>
    public static byte[] RenderIco(Document document, IReadOnlyList<int> sizes, bool includeBackground, int page = 0, bool onlyDrawing = false)
    {
        // the part of the drawing that goes into every frame: the whole page, or - with only the drawing - the part where something is drawn
        var area = SKRect.Create(0, document.PageTop(page), document.Width, document.Height);
        if (onlyDrawing)
        {
            var drawn = DrawnArea(document, page);
            area = SKRect.Create(drawn.Left, document.PageTop(page) + drawn.Top, drawn.Width, drawn.Height);
            includeBackground = false;
        }

        var frames = new List<(int Size, byte[] Png)>();
        foreach (var size in sizes.Distinct().Where(s => s is >= 1 and <= 256).OrderBy(s => s))
        {
            using var square = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(square);
            canvas.Clear(SKColors.Transparent);

            var scale = size / Math.Max(area.Width, area.Height);
            canvas.Translate((size - area.Width * scale) / 2f, (size - area.Height * scale) / 2f);
            canvas.Scale(scale, scale);
            canvas.Translate(-area.Left, -area.Top);
            canvas.ClipRect(area);
            DocumentRenderer.Render(canvas, document, document.Width, document.Height, null, forExport: true, includeBackground);

            frames.Add((size, Encode(square, SKEncodedImageFormat.Png, 100)));
        }

        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one icon size is required.", nameof(sizes));
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)png.Length);
            writer.Write((uint)offset);
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
        {
            writer.Write(png);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using (bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, quality);
            return data.ToArray();
        }
    }
}
