using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Icono.App.Views;

/// <summary>Builds a mouse cursor from a drawing, in memory (no file, no resource): the drawing is rendered once and packed as a cursor file.</summary>
public static class CursorBuilder
{
    /// <summary>The cursor of the picture <paramref name="visual"/> (a <paramref name="size"/> x <paramref name="size"/> square) with its hot spot - the
    /// pixel that clicks - at (<paramref name="hotX"/>, <paramref name="hotY"/>). A plain cross when the cursor cannot be built.</summary>
    public static Cursor Build(DrawingVisual visual, int size, int hotX, int hotY)
    {
        try
        {
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[size * size * 4];
            bitmap.CopyPixels(pixels, size * 4, 0);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // cursor file: header, one entry, the bitmap (colour rows bottom-up, then the empty AND mask)
            var maskBytes = size * 4;
            var imageBytes = 40 + size * size * 4 + maskBytes;
            writer.Write((ushort)0);
            writer.Write((ushort)2);
            writer.Write((ushort)1);
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)hotX);
            writer.Write((ushort)hotY);
            writer.Write((uint)imageBytes);
            writer.Write((uint)22);

            writer.Write(40u);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0u);
            writer.Write(0u);

            for (var y = size - 1; y >= 0; y--)
            {
                for (var x = 0; x < size; x++)
                {
                    var i = (y * size + x) * 4;
                    var alpha = pixels[i + 3];

                    // the bitmap is premultiplied, a cursor file wants plain colours
                    writer.Write(alpha == 0 ? (byte)0 : (byte)Math.Min(255, pixels[i] * 255 / alpha));
                    writer.Write(alpha == 0 ? (byte)0 : (byte)Math.Min(255, pixels[i + 1] * 255 / alpha));
                    writer.Write(alpha == 0 ? (byte)0 : (byte)Math.Min(255, pixels[i + 2] * 255 / alpha));
                    writer.Write(alpha);
                }
            }

            writer.Write(new byte[maskBytes]);
            writer.Flush();
            stream.Position = 0;
            return new Cursor(stream);
        }
        catch (Exception)
        {
            return Cursors.Cross;
        }
    }
}
