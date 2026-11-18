using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>An SVG or a picture is put into the middle of the current page, in the selected layer, selected, as one undo step - for the Windows
/// program and the tablet app alike.</summary>
public class ImageImportTests
{
    private const string Svg = "<svg viewBox=\"0 0 24 24\"><path d=\"M2 2 L22 2 L22 12 L2 12 Z\" fill=\"#ff0000\"/><path d=\"M2 14 L22 14 L22 22 L2 22 Z\" fill=\"#0000ff\"/></svg>";

    private static EditorContext NewContext()
    {
        var context = new EditorContext();
        context.Document.Width = 800;
        context.Document.Height = 600;
        context.Document.Layers.Add(new Layer { Name = "Layer 1" });
        return context;
    }

    private static byte[] Png(int width, int height, bool opaque = true)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(opaque ? SKColors.Orange : SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public void AnSvg_BecomesOneShapePerElement_SelectedInTheMiddleOfThePage()
    {
        var context = NewContext();

        var shapes = ImageImport.ImportSvg(context, Svg, out var error);

        Assert.Null(error);
        Assert.NotNull(shapes);
        Assert.Equal(2, shapes!.Count);
        Assert.Equal(shapes.Count, context.SelectedShapes.Count);
        Assert.All(shapes, s => Assert.Contains(s, context.Document.Layers[0].Shapes));
        var centreX = shapes.Average(s => s.X + s.Width / 2);
        Assert.InRange(centreX, 380, 420);
    }

    [Fact]
    public void AnSvgImport_IsOneUndoStep()
    {
        var context = NewContext();
        ImageImport.ImportSvg(context, Svg, out _);

        context.Undo();

        Assert.Empty(context.Document.Layers[0].Shapes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is no svg")]
    public void ACodeWithoutAPath_IsRefused_WithAMessage(string code)
    {
        var context = NewContext();

        var shapes = ImageImport.ImportSvg(context, code, out var error);

        Assert.Null(shapes);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Empty(context.Document.Layers[0].Shapes);
    }

    [Fact]
    public void APicture_BecomesAnImageShape_AtMost160OnItsLongSide()
    {
        var context = NewContext();

        var shape = ImageImport.ImportPicture(context, Png(640, 320), out var error);

        Assert.Null(error);
        Assert.NotNull(shape);
        Assert.Equal(ShapeKind.RasterImage, shape!.Kind);
        Assert.Equal(160, shape.Width, 3);
        Assert.Equal(80, shape.Height, 3);
        Assert.Equal(400, shape.X + shape.Width / 2, 3);
        Assert.Equal(300, shape.Y + shape.Height / 2, 3);
        Assert.Same(shape, Assert.Single(context.SelectedShapes));
        Assert.NotEmpty(shape.ImageBytes!);
    }

    [Fact]
    public void ASmallPicture_KeepsItsPixelSize()
    {
        var context = NewContext();

        var shape = ImageImport.ImportPicture(context, Png(40, 30), out _);

        Assert.Equal(40, shape!.Width, 3);
        Assert.Equal(30, shape.Height, 3);
    }

    [Fact]
    public void ABigPhoto_IsMadeSmaller_SoTheDrawingFileStaysSmall()
    {
        var context = NewContext();

        var shape = ImageImport.ImportPicture(context, Png(4000, 3000), out _);

        using var stored = SKBitmap.Decode(shape!.ImageBytes);
        Assert.Equal(ImageImport.MaxPictureSide, stored.Width);
        Assert.Equal(1536, stored.Height);
    }

    [Fact]
    public void APictureOnTheSecondPage_LandsOnThatPage()
    {
        var context = NewContext();
        context.Document.PageCount = 2;
        context.CurrentPage = 1;

        var shape = ImageImport.ImportPicture(context, Png(40, 40), out _);

        Assert.True(shape!.Y >= context.Document.PageTop(1));
    }

    [Fact]
    public void Garbage_IsNoPicture_AndChangesNothing()
    {
        var context = NewContext();

        var shape = ImageImport.ImportPicture(context, [1, 2, 3, 4, 5], out var error);

        Assert.Null(shape);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Empty(context.Document.Layers[0].Shapes);
    }

    [Fact]
    public void APictureImport_IsOneUndoStep()
    {
        var context = NewContext();
        ImageImport.ImportPicture(context, Png(40, 40), out _);

        context.Undo();

        Assert.Empty(context.Document.Layers[0].Shapes);
    }
}

public class ImageImportIcoTests
{
    [Fact]
    public void AnIcoFile_BecomesAnImageShape_FromItsLargestPicture()
    {
        var context = new EditorContext();
        context.Document.Width = 800;
        context.Document.Height = 600;
        context.Document.Layers.Add(new Layer { Name = "Layer 1" });
        using var bitmap = new SKBitmap(64, 64);
        bitmap.Erase(SKColors.Purple);
        using var image = SKImage.FromBitmap(bitmap);
        var png = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write(new byte[] { 0, 0, 1, 0, 1, 0, 64, 64, 0, 0, 1, 0, 32, 0 });
        writer.Write(png.Length);
        writer.Write(22);
        writer.Write(png);

        var shape = ImageImport.ImportPicture(context, file.ToArray(), out var error);

        Assert.Null(error);
        Assert.Equal(ShapeKind.RasterImage, shape!.Kind);
        Assert.Equal(64, shape.Width, 3);
    }
}
