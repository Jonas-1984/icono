using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

public class PlanSymbolTests
{
    private static readonly ShapeKind[] Kinds =
    [
        ShapeKind.Socket, ShapeKind.DoubleSocket, ShapeKind.ElectricAppliance, ShapeKind.Cooker, ShapeKind.Oven,
        ShapeKind.Microwave, ShapeKind.WaterAndWastewater, ShapeKind.Hood,
    ];

    private static Shape Symbol(ShapeKind kind, double x = 10, double y = 10, double size = 80)
    {
        var shape = new Shape { Kind = kind, X = x, Y = y, Width = size, Height = size };
        PlanSymbols.ApplyDefaults(shape);
        return shape;
    }

    [Fact]
    public void EverySymbolHasAnOutline()
    {
        foreach (var kind in Kinds)
        {
            Assert.True(PlanSymbols.IsPlanSymbol(kind));
            using var path = ShapeGeometryFactory.Build(Symbol(kind));
            Assert.False(path.IsEmpty, kind.ToString());
            Assert.True(path.Bounds.Width > 5 && path.Bounds.Height > 5, kind.ToString());
        }
    }

    [Fact]
    public void OtherKindsAreNoPlanSymbols()
    {
        Assert.False(PlanSymbols.IsPlanSymbol(ShapeKind.Rectangle));
        Assert.False(PlanSymbols.IsPlanSymbol(ShapeKind.DoorSingle));
    }

    [Fact]
    public void TheDotsAndTheE_AreSolidParts()
    {
        Assert.NotNull(ShapeGeometryFactory.BuildSolidParts(Symbol(ShapeKind.Cooker)));
        Assert.NotNull(ShapeGeometryFactory.BuildSolidParts(Symbol(ShapeKind.Oven)));
        Assert.NotNull(ShapeGeometryFactory.BuildSolidParts(Symbol(ShapeKind.ElectricAppliance)));
        Assert.Null(ShapeGeometryFactory.BuildSolidParts(Symbol(ShapeKind.Hood)));
    }

    [Fact]
    public void ACookerDrawsThreeSolidDots_InTheBorderColour()
    {
        var cooker = Symbol(ShapeKind.Cooker, 10, 10, 100);
        cooker.BorderColor = ArgbColor.FromRgb(0xC0, 0x00, 0x00);
        var document = new Document { Width = 140, Height = 140 };
        var layer = new Layer { Name = "L" };
        layer.Shapes.Add(cooker);
        document.Layers.Add(layer);

        using var bitmap = new SKBitmap(140, 140);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        DocumentRenderer.Render(canvas, document, 140, 140, forExport: false);

        // the top dot is at (12, 8.5) of the 24 x 24 template: solid red, not a white disc inside a ring
        var x = 10 + (int)(12 / 24.0 * 100);
        var y = 10 + (int)(8.5 / 24.0 * 100);
        var pixel = bitmap.GetPixel(x, y);
        Assert.True(pixel.Red > 150 && pixel.Green < 60, $"the dot is {pixel}");
    }

    [Fact]
    public void AClickedSymbol_HasItsRealSizeAtTheDrawingScale_InTheProportionOfTheSymbol()
    {
        var oven = PlanSymbols.DefaultSize(ShapeKind.Oven, 50, 794);
        var socket = PlanSymbols.DefaultSize(ShapeKind.Socket, 20, 794);

        Assert.Equal(60 / 2.54 * 96 / 50, oven.Width, 3);   // 60 cm at 1:50
        Assert.Equal(20 / 2.54 * 96 / 20, socket.Width, 3); // 20 cm at 1:20
        Assert.True(socket.Height < socket.Width);          // a half circle on a line is wider than high
    }

    [Fact]
    public void ASymbolIsRenderedWithoutError_InEveryKind()
    {
        var document = new Document { Width = 400, Height = 200 };
        var layer = new Layer { Name = "L" };
        var x = 10;
        foreach (var kind in Kinds)
        {
            layer.Shapes.Add(Symbol(kind, x, 20, 40));
            x += 46;
        }

        document.Layers.Add(layer);
        using var bitmap = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        DocumentRenderer.Render(canvas, document, 400, 200, forExport: false);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(
            @"C:\Users\shoja\AppData\Local\Temp\claude\g--Dev-icono\6700188b-447f-4e90-bb32-6f1e9b48a03d\scratchpad\plan_symbols.png", data.ToArray());
        Assert.True(data.Size > 500);
    }
}
