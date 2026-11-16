using System.Text;
using System.Text.RegularExpressions;
using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

public class PdfExportTests
{
    private static Document TwoA4Pages()
    {
        var document = new Document { Width = Document.A4Width, Height = Document.A4Height, PageCount = 2, BackgroundMode = BackgroundMode.SolidColor };
        var layer = new Layer { Name = "Layer 1" };
        layer.Shapes.Add(new Shape { Kind = ShapeKind.Rectangle, X = 100, Y = 100, Width = 200, Height = 100 });
        document.Layers.Add(layer);
        return document;
    }

    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void EveryPageBecomesAPdfPage_AtTheRealPaperSize()
    {
        var pdf = Text(DocumentExporter.RenderPdf(TwoA4Pages(), includeBackground: true));

        Assert.StartsWith("%PDF", pdf);
        Assert.Matches(@"/Count 2\b", pdf);

        // A4 = 794 x 1123 units at 96 per inch = 595.5 x 842.25 pt (210 x 297 mm); Skia writes the page size in whole points (the drawing
        // inside is scaled exactly, so the drawing scale stays true)
        var box = Regex.Match(pdf, @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]");
        Assert.True(box.Success);
        Assert.InRange(double.Parse(box.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 595, 596.5);
        Assert.InRange(double.Parse(box.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), 841.5, 843);
    }

    [Fact]
    public void ChosenPagesOnly()
    {
        var pdf = Text(DocumentExporter.RenderPdf(TwoA4Pages(), includeBackground: false, pages: [1]));

        Assert.Matches(@"/Count 1\b", pdf);
    }
}
