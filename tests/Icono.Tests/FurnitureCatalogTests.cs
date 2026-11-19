using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class FurnitureCatalogTests
{
    private static IEnumerable<UnitObject> Pieces() =>
        UnitCatalog.All.Where(u => u.Group is UnitCatalog.FurnitureGroup or UnitCatalog.BathroomGroup);

    [Fact]
    public void TheCatalogue_HasFurnitureAndBathroomObjects()
    {
        Assert.True(Pieces().Count(u => u.Group == UnitCatalog.FurnitureGroup) >= 15);
        Assert.True(Pieces().Count(u => u.Group == UnitCatalog.BathroomGroup) >= 6);
        Assert.Contains(Pieces(), u => u.Name.StartsWith("Bed 140"));
        Assert.Contains(Pieces(), u => u.Name.StartsWith("Toilet"));
    }

    [Fact]
    public void EveryObject_HasAUniqueIdAndName_AndARealSize()
    {
        var all = UnitCatalog.All;
        Assert.Equal(all.Count, all.Select(u => u.Id).Distinct().Count());
        Assert.Equal(Pieces().Count(), Pieces().Select(u => u.Name).Distinct().Count());
        Assert.All(Pieces(), u =>
        {
            Assert.InRange(u.WidthCm, 30, 250);
            Assert.InRange(u.HeightCm, 30, 250);
            Assert.EndsWith("(plan)", u.Name);
        });
    }

    [Fact]
    public void EveryDrawing_IsValid_AndFillsItsBox()
    {
        foreach (var unit in Pieces())
        {
            using var path = SKPath.ParseSvgPathData(unit.PathData);
            Assert.True(path is not null, unit.Name);
            var bounds = path!.TightBounds;
            Assert.True(bounds.Left >= -0.05 && bounds.Top >= -0.05 && bounds.Right <= 24.05 && bounds.Bottom <= 24.05, $"{unit.Name} {bounds}");
            // the outline reaches all four sides of the 24 x 24 box, so the size in cm is the size of what is drawn
            Assert.True(bounds.Left < 0.5 && bounds.Top < 0.5 && bounds.Right > 23.5 && bounds.Bottom > 23.5, $"{unit.Name} {bounds}");
        }
    }

    [Fact]
    public void APlacedObject_IsDrawnAtItsRealSize()
    {
        var bed = UnitCatalog.All.First(u => u.Name.StartsWith("Bed 140"));
        var document = new Document { Width = 794, Height = 1123, ScaleDenominator = 20 };
        var layer = new Layer { Name = "L" };
        var shape = ShapeTemplates.NewBox(ShapeKind.CombinedPath, 100, 100, ArgbColor.FromRgb(255, 255, 255), bed);
        ShapeTemplates.ApplyClickSize(shape, document, bed);
        layer.Shapes.Add(shape);
        document.Layers.Add(layer);

        var expectedWidth = 140 / 2.54 * 96 / 20;
        var expectedHeight = 200 / 2.54 * 96 / 20;
        Assert.Equal(expectedWidth, shape.Width, 1);
        Assert.Equal(expectedHeight, shape.Height, 1);

        using var bitmap = DocumentExporter.RenderBitmap(document, 794, 1123, includeBackground: false);
        var drawn = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                drawn += bitmap.GetPixel(x, y).Alpha > 0 ? 1 : 0;
            }
        }

        Assert.True(drawn > 300, $"drawn pixels {drawn}");
    }
}
