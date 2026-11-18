using Icono.Core;
using Icono.Editor;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class PolylineClosingTests
{
    private static (EditorContext Context, Layer Layer, Shape Polyline) NewPolyline(int points = 4)
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        var polyline = ShapeTemplates.NewTwoPointLine(ShapeKind.Polyline, 20, 20, ArgbColor.FromRgb(230, 20, 20));
        polyline.Kind = ShapeKind.Polyline;
        polyline.FillEnabled = false;
        polyline.Points = new[] { new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 80), new CorePoint(0, 80) }.Take(points).ToList();
        PathBoundsCalculator.Recompute(polyline);
        layer.Shapes.Add(polyline);
        return (context, layer, polyline);
    }

    [Fact]
    public void AnOpenPolylineWithThreePoints_CanBeClosed_ATwoPointOneCannot()
    {
        Assert.True(PolylineClosing.CanClose(NewPolyline(3).Polyline));
        Assert.False(PolylineClosing.CanClose(NewPolyline(2).Polyline));
    }

    [Fact]
    public void Closing_MakesAPolygon_WithFill_AndUndoGivesTheOpenPolylineBack()
    {
        var (context, _, polyline) = NewPolyline();
        context.SelectedShapes.Add(polyline);

        Assert.Equal(1, PolylineClosing.CloseSelected(context));

        Assert.Equal(ShapeKind.Polygon, polyline.Kind);
        Assert.True(polyline.FillEnabled);
        Assert.Equal(4, polyline.Points!.Count);

        context.Undo();
        Assert.Equal(ShapeKind.Polyline, polyline.Kind);
        Assert.False(polyline.FillEnabled);

        context.Redo();
        Assert.Equal(ShapeKind.Polygon, polyline.Kind);
    }

    [Fact]
    public void TheClosedShape_IsFilled_OnItsInside_AndBordersAllSides()
    {
        var (_, layer, polyline) = NewPolyline();
        polyline.FillColor = ArgbColor.FromRgb(20, 200, 40);
        polyline.BorderColor = ArgbColor.FromRgb(0, 0, 0);
        polyline.BorderThickness = 2;
        PolylineClosing.Close(polyline);
        var document = new Document { Width = 200, Height = 140 };
        document.Layers.Add(layer);

        using var bitmap = new SKBitmap(200, 140);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        DocumentRenderer.Render(canvas, document, 200, 140, forExport: true);

        var inside = bitmap.GetPixel(70, 60); // inside the 100 x 80 shape that starts at 20, 20
        Assert.True(inside.Green > 150 && inside.Red < 80, $"the inside is filled: {inside}");
        var closingSide = bitmap.GetPixel(20, 60); // the left side joins the last point with the first: only a closed shape has it
        Assert.True(closingSide.Red < 90 && closingSide.Green < 90, $"the closing side is drawn: {closingSide}");
    }

    [Fact]
    public void ALastPointOnTheFirstOne_IsDropped_WhenClosing()
    {
        var (_, _, polyline) = NewPolyline();
        polyline.Points = [new CorePoint(0, 0), new CorePoint(100, 0), new CorePoint(100, 80), new CorePoint(0, 0)];

        PolylineClosing.Close(polyline);

        Assert.Equal(3, polyline.Points!.Count);
    }

    [Fact]
    public void ThePolygonKind_IsNotAStrokeOnlyLine_SoFillAndHitTestsWork()
    {
        Assert.False(ShapeGeometryFactory.IsStrokeOnly(ShapeKind.Polygon));
        Assert.False(ShapeGeometryFactory.IsLineFamily(ShapeKind.Polygon));
    }
}
