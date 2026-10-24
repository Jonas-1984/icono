using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Renders a <see cref="Document"/> to image files: PNG at any size, and multi-size Windows .ico files.</summary>
public static class DocumentExporter
{
    /// <summary>Standard sizes of a Windows icon, smallest first.</summary>
    public static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>Renders the page so that its LONGER side is <paramref name="longSide"/> px (the aspect ratio is kept).
    /// With <paramref name="includeBackground"/> the page background (colour / blur / picture) is part of the image,
    /// otherwise the result is transparent.</summary>
    public static byte[] RenderPng(Document document, int longSide, bool includeBackground)
    {
        var scale = longSide / (double)Math.Max(document.Width, document.Height);
        var width = Math.Max(1, (int)Math.Round(document.Width * scale));
        var height = Math.Max(1, (int)Math.Round(document.Height * scale));
        return Encode(RenderBitmap(document, width, height, includeBackground), SKEncodedImageFormat.Png, 100);
    }

    /// <summary>Renders the page at an exact pixel size.</summary>
    public static SKBitmap RenderBitmap(Document document, int width, int height, bool includeBackground)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(width / (float)document.Width, height / (float)document.Height);
        DocumentRenderer.Render(canvas, document, document.Width, document.Height, null, forExport: true, includeBackground);
        return bitmap;
    }

    /// <summary>Builds a .ico with one PNG frame per size. The page is fitted (centred, aspect ratio kept) into each
    /// square frame; the rest of the frame stays transparent.</summary>
    public static byte[] RenderIco(Document document, IReadOnlyList<int> sizes, bool includeBackground)
    {
        var frames = new List<(int Size, byte[] Png)>();
        foreach (var size in sizes.Distinct().Where(s => s is >= 1 and <= 256).OrderBy(s => s))
        {
            using var square = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(square);
            canvas.Clear(SKColors.Transparent);

            var scale = size / (float)Math.Max(document.Width, document.Height);
            canvas.Translate((size - document.Width * scale) / 2f, (size - document.Height * scale) / 2f);
            canvas.Scale(scale, scale);
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
