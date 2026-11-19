using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class OnlyDrawingExportTests
{
    private static Document PageWithBox(int pageCount = 1, int onPage = 0)
    {
        var document = new Document { Width = 794, Height = 1123, PageCount = pageCount, BackgroundMode = BackgroundMode.SolidColor };
        document.BackgroundColor = ArgbColor.FromRgb(255, 255, 255);
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(new Shape
        {
            Kind = ShapeKind.Rectangle, X = 100, Y = document.PageTop(onPage) + 200, Width = 80, Height = 40,
            FillColor = ArgbColor.FromRgb(220, 30, 30), BorderThickness = 0, FillEnabled = true,
        });
        document.Layers.Add(layer);
        return document;
    }

    [Fact]
    public void TheDrawnArea_IsWhereTheBoxIs_NotThePage()
    {
        var area = DocumentExporter.DrawnArea(PageWithBox());

        // the box is drawn with a little room inside its frame (100, 200, 80 x 40): the area is where the colour really is
        Assert.InRange(area.Left, 100, 115);
        Assert.InRange(area.Top, 200, 212);
        Assert.InRange(area.Width, 55, 75);
        Assert.InRange(area.Height, 24, 38);
    }

    [Fact]
    public void ThePng_HasTheSizeOfTheDrawing_AndNoBackground()
    {
        var document = PageWithBox();

        var png = DocumentExporter.RenderPng(document, 400, includeBackground: true, page: 0, onlyDrawing: true);

        using var bitmap = SKBitmap.Decode(png);
        Assert.True(bitmap.Width <= 400 && bitmap.Width > 300, $"width {bitmap.Width}");
        Assert.True(bitmap.Height < bitmap.Width, "the drawing is wider than high");
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha); // the white page is not part of it, though the background was asked for
        var middle = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(middle.Red > 200 && middle.Green < 60 && middle.Alpha == 255, $"{middle}");
    }

    [Fact]
    public void AnotherPage_HasItsOwnArea_AndAllPagesTogetherAreMeasuredFromTheTop()
    {
        var document = PageWithBox(pageCount: 2, onPage: 1);

        var onPage = DocumentExporter.DrawnArea(document, 1);
        var onAll = DocumentExporter.DrawnArea(document);

        Assert.InRange(onPage.Top, 200, 212); // relative to its own page
        Assert.True(onAll.Top > document.PageTop(1), "relative to the top of all pages");
        Assert.Throws<InvalidOperationException>(() => DocumentExporter.DrawnArea(document, 0)); // nothing on page 1
    }

    [Fact]
    public void NothingDrawn_ThereIsNothingToExport()
    {
        var document = new Document { Width = 300, Height = 200 };
        document.Layers.Add(new Layer { Name = "L" });

        Assert.Throws<InvalidOperationException>(() => DocumentExporter.RenderPng(document, 100, true, 0, onlyDrawing: true));
    }

    [Fact]
    public void TheSvg_HasTheSizeOfTheDrawing()
    {
        var svg = System.Text.Encoding.UTF8.GetString(DocumentExporter.RenderSvg(PageWithBox(), includeBackground: true, page: 0, onlyDrawing: true));

        var width = int.Parse(System.Text.RegularExpressions.Regex.Match(svg, "width=\"(\\d+)\"").Groups[1].Value);
        Assert.InRange(width, 55, 80);
        Assert.DoesNotContain("width=\"794\"", svg);
    }

    [Fact]
    public void TheIcon_AndThePngSet_AreMadeOfTheDrawing()
    {
        var document = PageWithBox();

        var ico = DocumentExporter.RenderIco(document, [32, 64], includeBackground: true, page: 0, onlyDrawing: true);
        var zip = DocumentExporter.RenderPngSetZip(document, "box", includeBackground: true, page: 0, onlyDrawing: true);

        Assert.True(ico.Length > 100);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        using var stream = archive.GetEntry("box_64.png")!.Open();
        using var picture = SKBitmap.Decode(stream);
        Assert.Equal(64, picture.Width);
        Assert.Equal(0, picture.GetPixel(0, 0).Alpha);
        Assert.True(picture.GetPixel(32, 16).Alpha > 0, "the box fills the width: it is in the middle of the picture");
    }
}
