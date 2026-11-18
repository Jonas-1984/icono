using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>Windows icon files: Skia cannot read the container, icono reads the largest picture itself (PNG inside, or the old bitmap kind).</summary>
public class IcoReaderTests
{
    /// <summary>A file with one directory entry per picture (<paramref name="images"/> are the raw bytes of each).</summary>
    private static byte[] Ico(params (int Size, int Bits, byte[] Image)[] images)
    {
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)images.Length);
        var offset = 6 + images.Length * 16;
        foreach (var (size, bits, image) in images)
        {
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)bits);
            writer.Write(image.Length);
            writer.Write(offset);
            offset += image.Length;
        }

        foreach (var (_, _, image) in images)
        {
            writer.Write(image);
        }

        return file.ToArray();
    }

    private static byte[] Png(int size, SKColor colour)
    {
        using var bitmap = new SKBitmap(size, size);
        bitmap.Erase(colour);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>The old kind: header, colours bottom row first, then the 1-bit mask (1 = transparent). 32-bit: BGRA per pixel.</summary>
    private static byte[] Dib32(int size, SKColor colour, byte alpha, bool maskTopLeft = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        for (var i = 0; i < size * size; i++)
        {
            writer.Write(colour.Blue);
            writer.Write(colour.Green);
            writer.Write(colour.Red);
            writer.Write(alpha);
        }

        var stride = (size + 31) / 32 * 4;
        for (var row = size - 1; row >= 0; row--) // bottom row first
        {
            var line = new byte[stride];
            if (maskTopLeft && row == 0)
            {
                line[0] = 0x80; // the left pixel of the top row is transparent
            }

            writer.Write(line);
        }

        return stream.ToArray();
    }

    private static byte[] Dib8(int size, SKColor colour)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(2); // two colours in the palette
        writer.Write(0);
        writer.Write([0, 0, 0, 0]);
        writer.Write(new byte[] { colour.Blue, colour.Green, colour.Red, 0 });
        var stride = (size + 3) / 4 * 4;
        for (var row = 0; row < size; row++)
        {
            var line = new byte[stride];
            Array.Fill(line, (byte)1, 0, size);
            writer.Write(line);
        }

        writer.Write(new byte[(size + 31) / 32 * 4 * size]); // mask: all opaque
        return stream.ToArray();
    }

    [Fact]
    public void IsIco_RecognisesTheHeader()
    {
        Assert.True(IcoReader.IsIco(Ico((16, 32, Png(16, SKColors.Red)))));
        Assert.False(IcoReader.IsIco(Png(16, SKColors.Red)));
        Assert.False(IcoReader.IsIco([]));
    }

    [Fact]
    public void ThePng_InsideTheFile_IsRead()
    {
        using var bitmap = IcoReader.LargestFrame(Ico((32, 32, Png(32, SKColors.Red))));

        Assert.NotNull(bitmap);
        Assert.Equal(32, bitmap!.Width);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 5));
    }

    [Fact]
    public void OfSeveralPictures_TheLargestIsTaken()
    {
        var file = Ico((16, 32, Png(16, SKColors.Red)), (48, 32, Png(48, SKColors.Blue)), (32, 32, Png(32, SKColors.Green)));

        using var bitmap = IcoReader.LargestFrame(file);

        Assert.Equal(48, bitmap!.Width);
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void ABitmapWith32Bits_KeepsItsAlpha()
    {
        using var bitmap = IcoReader.LargestFrame(Ico((16, 32, Dib32(16, new SKColor(10, 200, 30), 128))));

        Assert.Equal(16, bitmap!.Width);
        var pixel = bitmap.GetPixel(3, 3);
        Assert.Equal(10, pixel.Red);
        Assert.Equal(200, pixel.Green);
        Assert.Equal(30, pixel.Blue);
        Assert.Equal(128, pixel.Alpha);
    }

    [Fact]
    public void ABitmapWithoutAlpha_IsOpaque_AndTheMaskMakesHoles()
    {
        using var bitmap = IcoReader.LargestFrame(Ico((16, 32, Dib32(16, SKColors.Orange, alpha: 0, maskTopLeft: true))));

        Assert.Equal(255, bitmap!.GetPixel(5, 5).Alpha);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);       // the masked pixel
        Assert.Equal(255, bitmap.GetPixel(1, 0).Alpha);
    }

    [Fact]
    public void ABitmapWithAPalette_IsRead()
    {
        using var bitmap = IcoReader.LargestFrame(Ico((16, 8, Dib8(16, new SKColor(200, 20, 40)))));

        Assert.NotNull(bitmap);
        Assert.Equal(new SKColor(200, 20, 40), bitmap!.GetPixel(7, 7).WithAlpha(255));
    }

    [Fact]
    public void ADamagedFile_GivesNothing_AndNoCrash()
    {
        var file = Ico((16, 32, Png(16, SKColors.Red)));

        Assert.Null(IcoReader.LargestFrame(file[..30]));                                       // cut off inside the picture
        Assert.Null(IcoReader.LargestFrame([0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0, 32, 0, 4, 0, 0, 0, 22, 0, 0, 0, 1, 2, 3, 4])); // no picture there
    }
}
