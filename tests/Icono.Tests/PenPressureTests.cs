using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class PenPressureTests
{
    private static List<SKPoint> Line(int count = 40) =>
        Enumerable.Range(0, count).Select(i => new SKPoint(20 + i * 5, 100 + (float)Math.Sin(i / 6.0) * 6)).ToList();

    private static (EditorContext Context, Layer Layer) NewContext()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        context.PenWidth = 4;
        return (context, layer);
    }

    [Fact]
    public void ThePressureFactor_GrowsWithThePressure_AndIsOneAtHalf()
    {
        Assert.Equal(1.0, FreehandBuilder.PressureFactor(0.5), 6);
        Assert.True(FreehandBuilder.PressureFactor(0.05) < 0.5);
        Assert.True(FreehandBuilder.PressureFactor(1) > 1.5);
        Assert.Equal(FreehandBuilder.PressureFactor(1), FreehandBuilder.PressureFactor(5), 6); // kept in range
    }

    [Fact]
    public void TheSensitivity_SetsHowMuchThePressureChangesTheWidth()
    {
        Assert.Equal(1.0, FreehandBuilder.PressureFactor(0.1, 0), 6); // 0: one width
        Assert.Equal(1.0, FreehandBuilder.PressureFactor(0.9, 0), 6);
        Assert.Equal(0.3, FreehandBuilder.PressureFactor(0, 0.7), 6); // the start: 0.3 to 1.7
        Assert.Equal(1.7, FreehandBuilder.PressureFactor(1, 0.7), 6);
        Assert.Equal(2.0, FreehandBuilder.PressureFactor(1, 1), 6); // 1: up to twice
        Assert.True(FreehandBuilder.PressureFactor(0, 1) > 0 && FreehandBuilder.PressureFactor(0, 1) < 0.1); // a hairline, never zero
    }

    [Fact]
    public void TheContextSensitivity_ReachesTheStroke()
    {
        var points = Enumerable.Range(0, 40).Select(i => new SKPoint(20 + i * 5, 100)).ToList();
        var pressure = points.Select((_, i) => i < 20 ? 0.1f : 0.95f).ToList();
        var flat = FreehandBuilder.CreatePressureShape(points, pressure, ArgbColor.FromRgb(0, 0, 0), 4, 0.5f, 0, strength: 0)!;
        var strong = FreehandBuilder.CreatePressureShape(points, pressure, ArgbColor.FromRgb(0, 0, 0), 4, 0.5f, 0, strength: 1)!;

        Assert.True(strong.Height > flat.Height + 3); // with 0 the line has one width, with 10 the hard end is far thicker

        var (context, layer) = NewContext();
        Assert.Equal(0.7, context.PenPressureStrength, 6);
        context.PenPressureStrength = 5;
        Assert.Equal(1, context.PenPressureStrength); // kept in range
    }

    [Fact]
    public void AStrokeWithPressure_IsAFilledOutline_WithItsCentreline()
    {
        var points = Line();
        var pressure = points.Select((_, i) => 0.2f + 0.7f * i / (points.Count - 1)).ToList();

        var shape = FreehandBuilder.CreatePressureShape(points, pressure, ArgbColor.FromRgb(10, 20, 30), 4, 0.5f)!;

        Assert.True(shape.Freehand);
        Assert.False(shape.CustomPathStrokeOnly);
        Assert.True(shape.FillEnabled);
        Assert.Equal(ArgbColor.FromRgb(10, 20, 30), shape.FillColor);
        Assert.NotNull(shape.InkCenterline);
        Assert.True(shape.Height > 8); // the sine of 12 px plus the thickness of the line
    }

    [Fact]
    public void TheLine_IsThickerWherePressedHarder()
    {
        var points = Enumerable.Range(0, 40).Select(i => new SKPoint(20 + i * 5, 100)).ToList();
        var pressure = points.Select((_, i) => i < 20 ? 0.1f : 0.95f).ToList();
        var shape = FreehandBuilder.CreatePressureShape(points, pressure, ArgbColor.FromRgb(0, 0, 0), 4, 0.5f)!;
        using var path = ShapeGeometryFactory.Build(shape);

        // 4 px at half pressure: a light touch is about 1.5 px, a hard one about 6.8 px
        Assert.True(path.Contains(40, 100));
        Assert.False(path.Contains(40, 101.6f));
        Assert.True(path.Contains(180, 102.5f));
        Assert.False(path.Contains(180, 104.5f));
    }

    [Fact]
    public void ADot_WithPressure_IsAFilledRoundDot()
    {
        var shape = FreehandBuilder.CreatePressureShape([new SKPoint(50, 50)], [0.9f], ArgbColor.FromRgb(0, 0, 0), 6, 0.5f)!;

        Assert.True(shape.Freehand);
        Assert.Null(shape.InkCenterline);
        Assert.True(shape.Width > 6);
    }

    [Fact]
    public void WithoutDifferentPressures_TheStrokeKeepsOneWidth()
    {
        var (context, layer) = NewContext();
        var points = Line();

        InkEditing.Commit(context, points, 1, points.Select(_ => 0.5f).ToList());
        InkEditing.Commit(context, points, 1);
        InkEditing.Commit(context, points, 1, points.Select((_, i) => 0.1f + 0.8f * i / points.Count).ToList());

        Assert.Equal(3, layer.Shapes.Count);
        Assert.True(layer.Shapes[2].CustomPathStrokeOnly); // constant pressure: as before
        Assert.True(layer.Shapes[1].CustomPathStrokeOnly);
        Assert.False(layer.Shapes[0].CustomPathStrokeOnly); // pressure: an outline
    }

    [Fact]
    public void WhenThePressureIsSwitchedOff_EveryStrokeKeepsOneWidth()
    {
        var (context, layer) = NewContext();
        context.PenPressure = false;
        var points = Line();

        InkEditing.Commit(context, points, 1, points.Select((_, i) => 0.1f + 0.8f * i / points.Count).ToList());

        Assert.True(Assert.Single(layer.Shapes).CustomPathStrokeOnly);
    }

    [Fact]
    public void TheEraser_TouchesAThickStroke_AnywhereInsideIt()
    {
        var (context, layer) = NewContext();
        context.PenWidth = 30;
        var points = Enumerable.Range(0, 30).Select(i => new SKPoint(20 + i * 5, 100)).ToList();
        InkEditing.Commit(context, points, 1, points.Select(_ => 0.9f).Concat([0.1f]).Take(30).Select((p, i) => i % 2 == 0 ? 0.9f : 0.8f).ToList());
        var shape = Assert.Single(layer.Shapes);
        Assert.False(shape.CustomPathStrokeOnly);

        var removed = InkEditing.EraseAt(context, 90, 100, 1, []); // the middle of a stroke far thicker than the reach of the eraser

        Assert.Equal(1, removed);
        Assert.Empty(layer.Shapes);
    }

    [Fact]
    public void TheWriting_IsReadFromTheCentreline_NotFromTheOutline()
    {
        var (context, layer) = NewContext();
        var points = Enumerable.Range(0, 30).Select(i => new SKPoint(20 + i * 5, 100)).ToList();
        InkEditing.Commit(context, points, 1, points.Select((_, i) => 0.1f + 0.8f * i / 30).ToList());

        var sampled = HandwritingToText.Sample(HandwritingToText.Strokes(context));

        var stroke = Assert.Single(sampled);
        Assert.All(stroke, p => Assert.InRange(p.Y, 99, 101)); // along the middle: an outline would also give points 2 px beside it
        Assert.InRange(stroke[0].X, 15, 25);
        Assert.InRange(stroke[^1].X, 160, 170);
    }

    [Fact]
    public void TheCentreline_StaysWithTheStroke_InAnUndoSnapshot()
    {
        var shape = FreehandBuilder.CreatePressureShape(Line(), Line().Select((_, i) => 0.2f + i * 0.01f).ToList(), ArgbColor.FromRgb(0, 0, 0), 4, 0.5f)!;

        var copy = new Shape { Kind = ShapeKind.CombinedPath };
        ShapeSnapshot.Capture(shape).ApplyTo(copy);

        Assert.Equal(shape.InkCenterline, copy.InkCenterline);
    }
}
