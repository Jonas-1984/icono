using System.Text;
using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class SvgExportTests
{
    private static Document Sample(int pages = 1)
    {
        var document = new Document { Width = 300, Height = 200, PageCount = pages, BackgroundMode = BackgroundMode.SolidColor, BackgroundColor = ArgbColor.FromRgb(250, 240, 230) };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(new Shape { Kind = ShapeKind.Rectangle, X = 20, Y = 20, Width = 100, Height = 60, FillColor = ArgbColor.FromRgb(255, 0, 0) });
        layer.Shapes.Add(new Shape { Kind = ShapeKind.Ellipse, X = 20, Y = document.PageTop(pages - 1) + 20, Width = 80, Height = 80, FillColor = ArgbColor.FromRgb(0, 0, 255) });
        document.Layers.Add(layer);
        return document;
    }

    private static string Svg(Document document, bool background = true, int? page = null) =>
        Encoding.UTF8.GetString(DocumentExporter.RenderSvg(document, background, page));

    [Fact]
    public void ExportsAnSvgDocument_WithThePageSizeAndTheShapes()
    {
        var svg = Svg(Sample());

        Assert.Contains("<svg", svg);
        Assert.Contains("width=\"300\"", svg);
        Assert.Contains("height=\"200\"", svg);
        Assert.Contains("<path", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void WithoutTheBackground_ThePageColourIsNotInTheFile()
    {
        Assert.Contains("#FAF0E6", Svg(Sample(), background: true));
        Assert.DoesNotContain("#FAF0E6", Svg(Sample(), background: false));
    }

    [Fact]
    public void OnePageOfSeveral_HasThePageSize_AllPagesTheWholeStack()
    {
        var document = Sample(3);

        Assert.Contains($"height=\"{document.Height}\"", Svg(document, page: 1));
        Assert.Contains($"height=\"{document.TotalHeight}\"", Svg(document));
    }
}
