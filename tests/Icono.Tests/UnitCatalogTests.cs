using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class UnitCatalogTests
{
    [Fact]
    public void TheCataloguesHaveAllGroupsWithDistinctIdsAndNames()
    {
        Assert.Equal(24, UnitCatalog.All.Count(u => u.Group == UnitCatalog.CabinetsGroup));
        Assert.Equal(8, UnitCatalog.All.Count(u => u.Group == "Sinks"));
        Assert.Equal(11, UnitCatalog.All.Count(u => u.Group == "Kitchen Elements"));
        Assert.Equal(3, UnitCatalog.All.Count(u => u.Group == "Computers"));
        Assert.Equal(UnitCatalog.All.Count, UnitCatalog.All.Select(u => u.Id).Distinct().Count());
        Assert.Equal(UnitCatalog.All.Count, UnitCatalog.All.Select(u => u.Name).Distinct().Count());
    }

    [Fact]
    public void EveryUnitHasARealSizeAndADrawingInsideTheTemplateBox()
    {
        foreach (var unit in UnitCatalog.All)
        {
            Assert.InRange(unit.WidthCm, 3, 300);
            Assert.InRange(unit.HeightCm, 3, 250);

            using var path = SKPath.ParseSvgPathData(unit.PathData);
            Assert.NotNull(path);
            Assert.InRange(path!.Bounds.Left, -0.01f, 0.4f);
            Assert.InRange(path.Bounds.Top, -0.01f, 0.4f);
            Assert.InRange(path.Bounds.Right, 23.6f, 24.01f);
            Assert.InRange(path.Bounds.Bottom, 23.6f, 24.01f);
        }
    }

    [Fact]
    public void AClickedUnit_HasItsRealSizeAtTheDrawingScale()
    {
        var cabinet = UnitCatalog.All.First(u => u.Name.StartsWith("Floor cabinet 60 cm (plan)"));

        var size = UnitCatalog.DefaultSize(cabinet, 50);

        Assert.Equal(cabinet.WidthCm / 2.54 * 96 / 50, size.Width, 3);
        Assert.Equal(cabinet.HeightCm / 2.54 * 96 / 50, size.Height, 3);
    }

    [Fact]
    public void AUnitIsDrawnAsACombinedPathInItsProportion()
    {
        var tall = UnitCatalog.All.First(u => u.HeightCm > 200);
        var shape = new Shape
        {
            Kind = ShapeKind.CombinedPath, CustomPathData = tall.PathData, CustomPathStrokeOnly = true,
            X = 10, Y = 10, Width = tall.WidthCm, Height = tall.HeightCm,
        };

        using var path = ShapeGeometryFactory.Build(shape);

        Assert.Equal(tall.WidthCm, path.Bounds.Width, 1);
        Assert.Equal(tall.HeightCm, path.Bounds.Height, 1);
    }

    [Fact]
    public void FindReturnsTheUnitById()
    {
        Assert.Same(UnitCatalog.All[3], UnitCatalog.Find(UnitCatalog.All[3].Id));
        Assert.Null(UnitCatalog.Find("nothing"));
    }
}
