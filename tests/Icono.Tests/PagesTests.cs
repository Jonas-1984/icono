using Icono.Core;
using Icono.IO;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

/// <summary>A project can have several pages, one below the other.</summary>
public class PagesTests
{
    private static Document ThreePages()
    {
        var document = new Document { Width = 200, Height = 300, PageCount = 3 };
        document.Layers.Add(new Layer { Name = "L" });
        return document;
    }

    [Fact]
    public void Pages_LieBelowEachOtherWithAGap()
    {
        var document = ThreePages();

        Assert.Equal(0, document.PageTop(0));
        Assert.Equal(300 + Document.PageGap, document.PageTop(1));
        Assert.Equal(2 * (300 + Document.PageGap), document.PageTop(2));
        Assert.Equal(3 * 300 + 2 * Document.PageGap, document.TotalHeight);
    }

    [Fact]
    public void PageIndexAt_FindsThePageOfAPosition_AndTheNearestOneInTheGaps()
    {
        var document = ThreePages();

        Assert.Equal(0, document.PageIndexAt(10));
        Assert.Equal(1, document.PageIndexAt(document.PageTop(1) + 150));
        Assert.Equal(2, document.PageIndexAt(document.PageTop(2) + 299));
        Assert.Equal(0, document.PageIndexAt(-500));
        Assert.Equal(2, document.PageIndexAt(100000));
        Assert.Equal(1, document.PageIndexAt(300 + Document.PageGap - 1)); // in the gap, nearer to the second page
    }

    [Fact]
    public void PageCount_IsKeptBetweenOneAndTheMaximum()
    {
        var document = new Document { PageCount = 0 };
        Assert.Equal(1, document.PageCount);
        document.PageCount = 5000;
        Assert.Equal(Document.MaxPages, document.PageCount);
    }

    [Fact]
    public void ThePageCount_SurvivesSaveAndLoad_AndOldFilesHaveOnePage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"icono-pages-{Guid.NewGuid():N}{DocumentFile.Extension}");
        try
        {
            DocumentFile.Save(ThreePages(), path);
            var loaded = new Document();
            DocumentFile.Load(path).ApplyTo(loaded);
            Assert.Equal(3, loaded.PageCount);
        }
        finally
        {
            File.Delete(path);
        }

        var old = new Document { PageCount = 4 };
        new DocumentFile { Width = 100, Height = 100 }.ApplyTo(old);
        Assert.Equal(1, old.PageCount);
    }

    [Fact]
    public void Export_OfOnePage_HasThePageSize_OfAllPages_TheWholeStack()
    {
        var document = ThreePages();

        Assert.Equal((200, 300), DocumentExporter.Extent(document, 1));
        Assert.Equal((200, document.TotalHeight), DocumentExporter.Extent(document, null));

        using var bitmap = DocumentExporter.RenderBitmap(document, 200, 300, includeBackground: false, page: 2);
        Assert.Equal(300, bitmap.Height);
    }

    [Fact]
    public void Export_OfAPage_ShowsWhatIsOnThatPage()
    {
        var document = ThreePages();
        var box = new Shape { Kind = ShapeKind.Rectangle, X = 20, Y = document.PageTop(1) + 20, Width = 60, Height = 60, FillColor = ArgbColor.FromRgb(255, 0, 0) };
        document.Layers[0].Shapes.Add(box);

        using var second = DocumentExporter.RenderBitmap(document, 200, 300, includeBackground: false, page: 1);
        using var first = DocumentExporter.RenderBitmap(document, 200, 300, includeBackground: false, page: 0);

        Assert.Equal(255, second.GetPixel(50, 50).Red);
        Assert.Equal(0, first.GetPixel(50, 50).Alpha);
    }

}
