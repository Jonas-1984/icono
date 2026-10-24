using Icono.Core;
using Icono.IO;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class SheetTests
{
    private static SheetInfo On(SheetPlacement placement) => new()
    {
        Enabled = true, Placement = placement, Project = "House Miller", Address = "Main Street 1, Hamburg", Date = "24.10.2026", Position = "A-01",
    };

    [Fact]
    public void ASheetThatIsOff_HasNoLayout()
    {
        Assert.Null(SheetRenderer.Layout(new SheetInfo(), 1, 794, 1123));
    }

    [Theory]
    [InlineData(SheetPlacement.Bottom)]
    [InlineData(SheetPlacement.Top)]
    [InlineData(SheetPlacement.Left)]
    [InlineData(SheetPlacement.Right)]
    public void TheTitleBlock_SitsInsideTheFrameAtTheChosenEdge(SheetPlacement placement)
    {
        var layout = SheetRenderer.Layout(On(placement), 40, 794, 1123)!;

        Assert.True(layout.Frame.Contains(layout.Block));
        switch (placement)
        {
            case SheetPlacement.Bottom: Assert.Equal(layout.Frame.Bottom, layout.Block.Bottom, 1); Assert.Equal(layout.Frame.Width, layout.Block.Width, 1); break;
            case SheetPlacement.Top: Assert.Equal(layout.Frame.Top, layout.Block.Top, 1); Assert.Equal(layout.Frame.Width, layout.Block.Width, 1); break;
            case SheetPlacement.Left: Assert.Equal(layout.Frame.Left, layout.Block.Left, 1); Assert.Equal(layout.Frame.Height, layout.Block.Height, 1); break;
            default: Assert.Equal(layout.Frame.Right, layout.Block.Right, 1); Assert.Equal(layout.Frame.Height, layout.Block.Height, 1); break;
        }
    }

    [Fact]
    public void TheFiveFields_FillTheBlockInOrder_AndTheScaleComesFromThePage()
    {
        var layout = SheetRenderer.Layout(On(SheetPlacement.Bottom), 40, 794, 1123)!;

        Assert.Equal(["PROJECT", "ADDRESS", "SCALE", "DATE", "POSITION"], layout.Cells.Select(c => c.Label));
        Assert.Equal("1:40", layout.Cells[2].Value);
        Assert.Equal("House Miller", layout.Cells[0].Value);
        Assert.Equal(layout.Block.Width, layout.Cells.Sum(c => c.Bounds.Width), 1);
        Assert.True(layout.Cells.Zip(layout.Cells.Skip(1)).All(p => p.First.Bounds.Right <= p.Second.Bounds.Left + 0.01f));

        var vertical = SheetRenderer.Layout(On(SheetPlacement.Left), 1, 794, 1123)!;
        Assert.Equal(vertical.Block.Height, vertical.Cells.Sum(c => c.Bounds.Height), 1);
    }

    [Fact]
    public void TheBlockSizeAndMargin_AreCustomisableButStayInsideThePage()
    {
        var custom = On(SheetPlacement.Bottom) with { MarginMm = 5, BlockSizeMm = 30 };
        var layout = SheetRenderer.Layout(custom, 1, 794, 1123)!;

        Assert.Equal(5 * SheetInfo.PxPerMm, layout.Frame.Left, 1);
        Assert.Equal(30 * SheetInfo.PxPerMm, layout.Block.Height, 1);

        var huge = SheetRenderer.Layout(custom with { BlockSizeMm = 5000 }, 1, 794, 1123)!;
        Assert.True(huge.Block.Height <= huge.Frame.Height * 0.45f + 0.01f);

        Assert.Null(SheetRenderer.Layout(custom with { MarginMm = 100 }, 1, 100, 100)); // no room left on the page
    }

    [Fact]
    public void ItIsDrawnOnThePage_FrameAndBlockText()
    {
        var document = new Document { Width = 794, Height = 1123, ScaleDenominator = 40, Sheet = On(SheetPlacement.Bottom) };
        document.Layers.Add(new Layer { Name = "L" });

        using var bitmap = new SKBitmap(794, 1123);
        using var canvas = new SKCanvas(bitmap);
        DocumentRenderer.Render(canvas, document, 794, 1123, forExport: true);

        var layout = SheetRenderer.Layout(document.Sheet, 40, 794, 1123)!;
        Assert.NotEqual(0, bitmap.GetPixel((int)layout.Frame.Left, 500).Alpha); // the frame line
        Assert.Equal(0, bitmap.GetPixel(2, 2).Alpha); // outside the frame nothing is drawn

        // the project text darkens its cell: some pixel inside it is not plain white
        var cell = layout.Cells[0].Bounds;
        var dark = false;
        for (var x = (int)cell.Left + 4; x < cell.Right - 4 && !dark; x++)
        {
            for (var y = (int)cell.Top + 14; y < cell.Bottom - 4 && !dark; y++)
            {
                dark = bitmap.GetPixel(x, y).Red < 120;
            }
        }

        Assert.True(dark, "the project name was not drawn");
    }

    [Fact]
    public void ASheetThatIsOff_DrawsNothing()
    {
        var document = new Document { Width = 200, Height = 200 };
        document.Layers.Add(new Layer { Name = "L" });
        using var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        DocumentRenderer.Render(canvas, document, 200, 200, forExport: true);

        Assert.Equal(0, bitmap.GetPixel(30, 30).Alpha);
    }

    [Fact]
    public void TheSheetSurvivesSavingAndLoading_AndOldFilesGetNone()
    {
        var document = new Document { Width = 300, Height = 200, Sheet = On(SheetPlacement.Right) with { BlockSizeMm = 42 } };
        document.Layers.Add(new Layer { Name = "L" });
        var path = Path.Combine(Path.GetTempPath(), $"sheet-{Guid.NewGuid():N}.icono");
        try
        {
            DocumentFile.Save(document, path);
            var loaded = new Document();
            DocumentFile.Load(path).ApplyTo(loaded);

            Assert.Equal(document.Sheet, loaded.Sheet);
        }
        finally
        {
            File.Delete(path);
        }

        var old = new Document { Sheet = On(SheetPlacement.Top) };
        new DocumentFile().ApplyTo(old);
        Assert.False(old.Sheet.Enabled);
    }
}
