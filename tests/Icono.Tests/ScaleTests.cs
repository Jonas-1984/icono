using Icono.Core;
using Icono.IO;
using Icono.Rendering;

namespace Icono.Tests;

/// <summary>These tests change the process-wide drawing scale, so they must not run alongside others that write lengths.</summary>
[CollectionDefinition("Scale", DisableParallelization = true)]
public class ScaleCollection;

[Collection("Scale")]
public class ScaleTests
{
    [Fact]
    public void Presets_IncludeTheCommonArchitecturalScales()
    {
        foreach (var scale in new double[] { 1, 20, 40, 50, 60 })
        {
            Assert.Contains(scale, Document.ScalePresets);
        }
    }

    [Fact]
    public void LengthsAreWrittenAtTheScale()
    {
        // 96 px = 1 inch = 2.54 cm on the page; at 1:50 that stands for 127 cm
        Assert.Equal("2.54 cm", DimensionRenderer.Format(96, LengthUnit.Centimeter, 2, scale: 1));
        Assert.Equal("127.00 cm", DimensionRenderer.Format(96, LengthUnit.Centimeter, 2, scale: 50));
        Assert.Equal("1.270 m", DimensionRenderer.Format(96, LengthUnit.Meter, 3, scale: 50));
        Assert.Equal("50.00 in", DimensionRenderer.Format(96, LengthUnit.Inch, 2, scale: 50));
        Assert.Equal("1270 mm", DimensionRenderer.Format(96, LengthUnit.Millimeter, 0, scale: 50));
        Assert.Equal("25.4 cm", WallGeometry.FormatLength(96, LengthUnit.Centimeter, scale: 10));
    }

    [Fact]
    public void ADimensionReadsTheDocumentScale()
    {
        var dim = new Shape { Kind = ShapeKind.Dimension, DimKind = DimensionKind.Aligned, X = 0, Y = 0 };
        DimensionBuilder.ApplyStyle(dim);
        dim.Points = [new CorePoint(0, 0), new CorePoint(96, 0), new CorePoint(0, 20)];
        PathBoundsCalculator.Recompute(dim);

        var before = DrawingScale.Denominator;
        try
        {
            DrawingScale.Denominator = 1;
            Assert.Equal("2.54 cm", DimensionRenderer.Layout(dim).Text);
            DrawingScale.Denominator = 50;
            Assert.Equal("127.00 cm", DimensionRenderer.Layout(dim).Text);
        }
        finally
        {
            DrawingScale.Denominator = before;
        }
    }

    [Fact]
    public void TheScaleIsSavedWithTheDocument()
    {
        var document = new Document { Width = 300, Height = 200, ScaleDenominator = 40 };
        document.Layers.Add(new Layer { Name = "L" });
        var path = Path.Combine(Path.GetTempPath(), $"icono-scale-{Guid.NewGuid():N}{DocumentFile.Extension}");

        var loaded = new Document();
        try
        {
            DocumentFile.Save(document, path);
            DocumentFile.Load(path).ApplyTo(loaded);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Equal(40, loaded.ScaleDenominator);
    }

    [Fact]
    public void RenderingADocumentUsesItsScaleAndPutsTheOldOneBack()
    {
        var document = new Document { Width = 200, Height = 100, ScaleDenominator = 50 };
        var layer = new Layer { Name = "L" };
        var dim = new Shape { Kind = ShapeKind.Dimension, DimKind = DimensionKind.Aligned, X = 20, Y = 20 };
        DimensionBuilder.ApplyStyle(dim);
        dim.Points = [new CorePoint(0, 0), new CorePoint(96, 0), new CorePoint(0, 30)];
        PathBoundsCalculator.Recompute(dim);
        layer.Shapes.Add(dim);
        document.Layers.Add(layer);

        var before = DrawingScale.Denominator;
        try
        {
            DrawingScale.Denominator = 1;
            using var bitmap = new SkiaSharp.SKBitmap(200, 100);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            DocumentRenderer.Render(canvas, document, 200, 100);

            Assert.Equal(1, DrawingScale.Denominator); // rendering another document did not change the live scale
        }
        finally
        {
            DrawingScale.Denominator = before;
        }
    }
}
