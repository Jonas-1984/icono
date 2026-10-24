using Icono.Core;
using Icono.IO;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class DocumentFileTests
{
    private static Document BuildSample()
    {
        var document = new Document { Width = 300, Height = 200, BackgroundMode = BackgroundMode.SolidColor };
        document.BackgroundColor = ArgbColor.FromRgb(0x10, 0x20, 0x30);

        var layer = new Layer { Name = "Art", Locked = true };
        layer.Shapes.Add(new Shape
        {
            Kind = ShapeKind.Rectangle, X = 10, Y = 20, Width = 80, Height = 40,
            FillColor = ArgbColor.FromRgb(200, 30, 30), DesignStyle = DesignStyleKind.Glassmorphism, Name = "Box",
        });
        layer.Shapes.Add(new Shape
        {
            Kind = ShapeKind.DesignGrid, X = 100, Y = 100, Width = 60, Height = 60,
            GridCells = new Dictionary<int, ArgbColor> { [DesignGridMath.Key(1, 2)] = ArgbColor.FromRgb(1, 2, 3) },
        });
        layer.Shapes.Add(new Shape
        {
            Kind = ShapeKind.CombinedPath, Width = 24, Height = 24, CustomPathData = "M0 0L24 0L24 24Z",
            FillGradient = new GradientFill(
                GradientKind.Linear, 0, 0, 1, 0, 0, 0, 0, 0, 0, 24, 0, 0, 24, 0, 0,
                [new GradientStop(0, ArgbColor.FromRgb(255, 0, 0)), new GradientStop(1, ArgbColor.FromRgb(0, 0, 255))]),
        });
        document.Layers.Add(layer);
        return document;
    }

    [Fact]
    public void SaveAndLoad_RoundTripsPageLayersShapesAndSpecialFills()
    {
        var path = Path.Combine(Path.GetTempPath(), $"icono-test-{Guid.NewGuid():N}{DocumentFile.Extension}");
        try
        {
            DocumentFile.Save(BuildSample(), path);

            var loaded = new Document();
            DocumentFile.Load(path).ApplyTo(loaded);

            Assert.Equal(300, loaded.Width);
            Assert.Equal(200, loaded.Height);
            Assert.Equal(BackgroundMode.SolidColor, loaded.BackgroundMode);
            Assert.Equal(ArgbColor.FromRgb(0x10, 0x20, 0x30), loaded.BackgroundColor);

            var layer = Assert.Single(loaded.Layers);
            Assert.Equal("Art", layer.Name);
            Assert.True(layer.Locked);
            Assert.Equal(3, layer.Shapes.Count);
            Assert.Equal(DesignStyleKind.Glassmorphism, layer.Shapes[0].DesignStyle);
            Assert.Equal("Box", layer.Shapes[0].Name);
            Assert.Equal(ArgbColor.FromRgb(1, 2, 3), layer.Shapes[1].GridCells![DesignGridMath.Key(1, 2)]);
            Assert.Equal(2, layer.Shapes[2].FillGradient!.Stops.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsGarbage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"icono-test-{Guid.NewGuid():N}{DocumentFile.Extension}");
        File.WriteAllText(path, "this is not json");
        try
        {
            Assert.Throws<InvalidDataException>(() => DocumentFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ApplyTo_AlwaysLeavesAtLeastOneLayer()
    {
        var document = new Document();
        new DocumentFile().ApplyTo(document);
        Assert.Single(document.Layers);
    }

    [Fact]
    public void RenderPng_KeepsAspectRatioAndOptionallyIncludesTheBackground()
    {
        var document = BuildSample();
        document.Layers.Clear();

        using var transparent = SKBitmap.Decode(DocumentExporter.RenderPng(document, 150, includeBackground: false));
        Assert.Equal(150, transparent.Width);
        Assert.Equal(100, transparent.Height);
        Assert.Equal(0, transparent.GetPixel(10, 10).Alpha);

        using var withBackground = SKBitmap.Decode(DocumentExporter.RenderPng(document, 150, includeBackground: true));
        Assert.Equal(255, withBackground.GetPixel(10, 10).Alpha);
        Assert.Equal(0x10, withBackground.GetPixel(10, 10).Red);
    }

    [Fact]
    public void RenderIco_ProducesAWellFormedMultiSizeIcon()
    {
        var bytes = DocumentExporter.RenderIco(BuildSample(), [16, 32, 256], includeBackground: true);

        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        Assert.Equal(3, BitConverter.ToUInt16(bytes, 4));
        Assert.Equal(0, bytes[6 + 32]); // the 256 px entry stores 0 as its width
    }
}
