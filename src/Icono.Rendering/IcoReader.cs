using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Reads Windows icon files (.ico) - Skia has no decoder for the container: an .ico holds several pictures of different sizes, each
/// either a PNG or an old-style bitmap (DIB) with a separate transparency mask. <see cref="LargestFrame"/> returns the biggest one.</summary>
public static class IcoReader
{
    /// <summary>True when the bytes start like an icon file (reserved 0, type 1, at least one picture).</summary>
    public static bool IsIco(byte[] data) =>
        data.Length >= 22 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0 && (data[4] | (data[5] << 8)) > 0;

    /// <summary>The picture of the largest size (the one with most colours among equals); null when the file is damaged or holds a format that is
    /// not read (compressed bitmaps).</summary>
    public static SKBitmap? LargestFrame(byte[] data)
    {
        if (!IsIco(data))
        {
            return null;
        }

        var count = data[4] | (data[5] << 8);
        (int Width, int Height, int Bits, int Size, int Offset)? best = null;
        for (var i = 0; i < count; i++)
        {
            var at = 6 + i * 16;
            if (at + 16 > data.Length)
            {
                break;
            }

            var width = data[at] == 0 ? 256 : data[at];
            var height = data[at + 1] == 0 ? 256 : data[at + 1];
            var bits = data[at + 6] | (data[at + 7] << 8);
            var size = BitConverter.ToInt32(data, at + 8);
            var offset = BitConverter.ToInt32(data, at + 12);
            if (size <= 0 || offset < 0 || (long)offset + size > data.Length)
            {
                continue;
            }

            if (best is not { } current || width * height > current.Width * current.Height
                || (width * height == current.Width * current.Height && bits > current.Bits))
            {
                best = (width, height, bits, size, offset);
            }
        }

        return best is { } frame ? Decode(data, frame.Offset, frame.Size) : null;
    }

    private static SKBitmap? Decode(byte[] data, int offset, int size)
    {
        // a picture of the new kind is a whole PNG file
        if (size > 8 && data[offset] == 0x89 && data[offset + 1] == 0x50 && data[offset + 2] == 0x4E && data[offset + 3] == 0x47)
        {
            using var png = SKData.CreateCopy(data.AsSpan(offset, size));
            using var codec = SKCodec.Create(png);
            return codec is null ? null : SKBitmap.Decode(codec);
        }

        return DecodeDib(data, offset, size);
    }

    /// <summary>The old kind: a bitmap header, the colours (bottom row first) and, below them, a 1-bit mask where a set bit means "transparent".</summary>
    private static SKBitmap? DecodeDib(byte[] data, int offset, int size)
    {
        if (size < 40 || BitConverter.ToInt32(data, offset) < 40)
        {
            return null;
        }

        var header = BitConverter.ToInt32(data, offset);
        var width = BitConverter.ToInt32(data, offset + 4);
        var height = BitConverter.ToInt32(data, offset + 8) / 2; // the height counts the colours and the mask
        var bits = BitConverter.ToInt16(data, offset + 14);
        var compression = BitConverter.ToInt32(data, offset + 16);
        var used = BitConverter.ToInt32(data, offset + 32);
        if (compression != 0 || width <= 0 || height <= 0 || width > 1024 || height > 1024 || bits is not (1 or 4 or 8 or 24 or 32))
        {
            return null;
        }

        var paletteCount = bits <= 8 ? (used > 0 ? used : 1 << bits) : 0;
        var palette = offset + header;
        var colours = palette + paletteCount * 4;
        var colourStride = (width * bits + 31) / 32 * 4;
        var maskStart = colours + colourStride * height;
        var maskStride = (width + 31) / 32 * 4;
        if (palette + paletteCount * 4 > offset + size || colours + colourStride * height > offset + size)
        {
            return null;
        }

        var hasMask = maskStart + maskStride * height <= offset + size;
        var pixels = new SKColor[width * height];
        var anyAlpha = false;
        for (var y = 0; y < height; y++)
        {
            var row = colours + (height - 1 - y) * colourStride; // bottom row first
            for (var x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;
                switch (bits)
                {
                    case 32:
                        b = data[row + x * 4];
                        g = data[row + x * 4 + 1];
                        r = data[row + x * 4 + 2];
                        a = data[row + x * 4 + 3];
                        anyAlpha |= a != 0;
                        break;
                    case 24:
                        b = data[row + x * 3];
                        g = data[row + x * 3 + 1];
                        r = data[row + x * 3 + 2];
                        break;
                    default:
                        var index = bits == 8
                            ? data[row + x]
                            : bits == 4 ? (data[row + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0xF : (data[row + x / 8] >> (7 - x % 8)) & 1;
                        if (index >= paletteCount)
                        {
                            return null;
                        }

                        b = data[palette + index * 4];
                        g = data[palette + index * 4 + 1];
                        r = data[palette + index * 4 + 2];
                        break;
                }

                pixels[y * width + x] = new SKColor(r, g, b, a);
            }
        }

        // a 32-bit picture whose alpha is all zero has no alpha: it is opaque, and only the mask makes holes
        var useAlpha = bits == 32 && anyAlpha;
        for (var y = 0; y < height; y++)
        {
            var mask = maskStart + (height - 1 - y) * maskStride;
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (!useAlpha && bits == 32)
                {
                    pixels[index] = pixels[index].WithAlpha(255);
                }

                if (!useAlpha && hasMask && ((data[mask + x / 8] >> (7 - x % 8)) & 1) == 1)
                {
                    pixels[index] = SKColors.Transparent;
                }
            }
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.Pixels = pixels;
        return bitmap;
    }
}
