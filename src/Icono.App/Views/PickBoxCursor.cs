using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Icono.App.Views;

/// <summary>The mouse cursor of the Curve and the Match tool: a small square (the pick box of a CAD program) with its middle on the hot spot.
/// It is drawn once, with a white edge so it reads on dark and light drawings, and built as a cursor file in memory.</summary>
public static class PickBoxCursor
{
    private const int Size = 32;

    private static Cursor? _instance;

    public static Cursor Instance => _instance ??= Build();

    private static Cursor Build()
    {
        try
        {
            var visual = new DrawingVisual();
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
            using (var dc = visual.RenderOpen())
            {
                var square = new Rect(10, 10, 12, 12);
                var halo = new Pen(Brushes.White, 4);
                var ink = new Pen(Brushes.Black, 2);
                dc.DrawRoundedRectangle(null, halo, square, 2, 2);
                dc.DrawRoundedRectangle(null, ink, square, 2, 2);
            }

            var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[Size * Size * 4];
            bitmap.CopyPixels(pixels, Size * 4, 0);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // cursor file: header, one entry (hot spot in the middle), the bitmap (colour rows bottom-up, then the empty AND mask)
            var maskBytes = Size * 4;
            var imageBytes = 40 + Size * Size * 4 + maskBytes;
            writer.Write((ushort)0);
            writer.Write((ushort)2);
            writer.Write((ushort)1);
            writer.Write((byte)Size);
            writer.Write((byte)Size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)(Size / 2));
            writer.Write((ushort)(Size / 2));
            writer.Write((uint)imageBytes);
            writer.Write((uint)22);

            writer.Write(40u);
            writer.Write(Size);
            writer.Write(Size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0u);
            writer.Write(0u);

            for (var y = Size - 1; y >= 0; y--)
            {
                for (var x = 0; x < Size; x++)
                {
                    var i = (y * Size + x) * 4;
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
