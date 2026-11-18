using Icono.Core;
using Icono.Editor;
using SkiaSharp;

namespace Icono.Tests;

[Collection("Scale")]
public class StrokeShapeRecognizerTests
{
    private static List<SKPoint> Circle(float cx, float cy, float r, float wobble = 0, float startAngle = 0.7f, float sweep = 6.4f)
    {
        var rng = new Random(7);
        var points = new List<SKPoint>();
        for (var i = 0; i <= 60; i++)
        {
            var a = startAngle + sweep * i / 60;
            var rr = r * (1 + (float)(rng.NextDouble() - 0.5) * wobble);
            points.Add(new SKPoint(cx + rr * MathF.Cos(a), cy + rr * MathF.Sin(a)));
        }

        return points;
    }

    private static List<SKPoint> Along(params (float X, float Y)[] corners)
    {
        var rng = new Random(3);
        var points = new List<SKPoint>();
        for (var i = 0; i + 1 < corners.Length; i++)
        {
            for (var step = 0; step < 20; step++)
            {
                var t = step / 20f;
                points.Add(new SKPoint(
                    corners[i].X + (corners[i + 1].X - corners[i].X) * t + (float)(rng.NextDouble() - 0.5) * 2,
                    corners[i].Y + (corners[i + 1].Y - corners[i].Y) * t + (float)(rng.NextDouble() - 0.5) * 2));
            }
        }

        points.Add(new SKPoint(corners[^1].X, corners[^1].Y));
        return points;
    }

    [Fact]
    public void ARoughCircle_BecomesACircle()
    {
        var result = StrokeShapeRecognizer.Recognize(Circle(200, 150, 80, wobble: 0.1f));

        Assert.NotNull(result);
        Assert.Equal(RecognizedKind.Ellipse, result!.Kind);
        Assert.Equal(result.Width, result.Height, 3); // a circle, not an oval
        Assert.InRange(result.Width, 140, 180);
        Assert.InRange(result.Center.X, 190, 210);
    }

    [Fact]
    public void AnOval_StaysAnOval()
    {
        var oval = Circle(0, 0, 80).Select(p => new SKPoint(p.X * 1.8f + 300, p.Y * 0.8f + 200)).ToList();

        var result = StrokeShapeRecognizer.Recognize(oval);

        Assert.NotNull(result);
        Assert.Equal(RecognizedKind.Ellipse, result!.Kind);
        Assert.True(result.Width > result.Height * 1.6);
    }

    [Fact]
    public void ARoughLine_BecomesAStraightLine_AndAlmostLevelBecomesLevel()
    {
        var wobbly = Along((10, 100), (200, 106));

        var result = StrokeShapeRecognizer.Recognize(wobbly);

        Assert.NotNull(result);
        Assert.Equal(RecognizedKind.Line, result!.Kind);
        Assert.Equal(result.Points[0].Y, result.Points[1].Y); // 1.8 degrees off: made level
    }

    [Fact]
    public void ADiagonalLine_KeepsItsDirection()
    {
        var result = StrokeShapeRecognizer.Recognize(Along((10, 10), (150, 120)));

        Assert.NotNull(result);
        Assert.Equal(RecognizedKind.Line, result!.Kind);
        Assert.NotEqual(result.Points[0].Y, result.Points[1].Y);
        Assert.NotEqual(result.Points[0].X, result.Points[1].X);
    }

    [Fact]
    public void ARoughRectangle_BecomesAnUprightRectangle()
    {
        var result = StrokeShapeRecognizer.Recognize(Along((50, 50), (250, 54), (252, 160), (48, 158), (52, 50)));

        Assert.NotNull(result);
        Assert.Equal(RecognizedKind.Rectangle, result!.Kind);
        Assert.Equal(0, result.Angle);
        Assert.InRange(result.Width, 185, 215);
        Assert.InRange(result.Height, 95, 125);
    }

    [Fact]
    public void ATurnedRectangle_KeepsItsTurn_ByLessThanFortyFiveDegrees()
    {
        // the 200 x 100 box turned by 30 degrees
        const float c = 0.8660254f;
        const float s = 0.5f;
        (float, float) Turn(float x, float y) => (300 + x * c - y * s, 300 + x * s + y * c);
        var result = StrokeShapeRecognizer.Recognize(Along(Turn(-100, -50), Turn(100, -50), Turn(100, 50), Turn(-100, 50), Turn(-100, -50)));

        Assert.NotNull(result);
        Assert.Equal(RecognizedKind.Rectangle, result!.Kind);
        Assert.InRange(Math.Abs(result.Angle), 25, 35);
        Assert.InRange(result.Width, 185, 215);
        Assert.InRange(result.Height, 90, 110);
    }

    [Fact]
    public void ARoughTriangle_BecomesAClosedThreeCornerShape()
    {
        var result = StrokeShapeRecognizer.Recognize(Along((150, 40), (260, 200), (40, 205), (152, 42)));

        Assert.NotNull(result);
        Assert.Equal(RecognizedKind.Polygon, result!.Kind);
        Assert.Equal(3, result.Points.Count);
    }

    [Fact]
    public void AScribble_AWord_OrATinyMark_AreLeftAlone()
    {
        var zigzag = Along((10, 10), (60, 80), (110, 10), (160, 80), (210, 10), (260, 80));

        Assert.Null(StrokeShapeRecognizer.Recognize(zigzag));
        Assert.Null(StrokeShapeRecognizer.Recognize(Along((10, 10), (14, 12))));
        Assert.Null(StrokeShapeRecognizer.Recognize([new SKPoint(5, 5), new SKPoint(5, 5), new SKPoint(6, 5)]));
    }

    [Fact]
    public void AnOpenArc_IsNotMadeIntoACircle()
    {
        Assert.Null(StrokeShapeRecognizer.Recognize(Circle(100, 100, 80, startAngle: 0, sweep: 3.2f)));
    }

    [Fact]
    public void TheShapes_AreRealObjects_InThePenColour_WithoutFill_AndOneUndoStep()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        context.PenColor = ArgbColor.FromRgb(200, 30, 30);

        Assert.True(StrokeShapes.Commit(context, StrokeShapeRecognizer.Recognize(Circle(200, 150, 80))!));
        Assert.True(StrokeShapes.Commit(context, StrokeShapeRecognizer.Recognize(Along((10, 100), (200, 100)))!));
        Assert.True(StrokeShapes.Commit(context, StrokeShapeRecognizer.Recognize(Along((150, 40), (260, 200), (40, 205), (152, 42)))!));

        Assert.Equal(3, layer.Shapes.Count);
        Assert.Contains(layer.Shapes, s => s.Kind == ShapeKind.Ellipse);
        Assert.Contains(layer.Shapes, s => s.Kind == ShapeKind.Line);
        Assert.Contains(layer.Shapes, s => s.Kind == ShapeKind.Polyline && s.Points!.Count == 4); // closed: the first corner again
        Assert.All(layer.Shapes, s =>
        {
            Assert.False(s.FillEnabled);
            Assert.Equal(ArgbColor.FromRgb(200, 30, 30), s.BorderColor);
        });

        context.Undo();
        Assert.Equal(2, layer.Shapes.Count);
    }

    [Fact]
    public void ADot_IsErasedByTheEraser()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        InkEditing.Commit(context, [new SKPoint(100, 100)], 1);
        Assert.Single(layer.Shapes);

        var erased = new List<(Layer Layer, Shape Shape, int Index)>();
        var removed = InkEditing.EraseAt(context, 101, 99, 1, erased);

        Assert.Equal(1, removed);
        Assert.Empty(layer.Shapes);
        InkEditing.CommitErase(context, erased);
        context.Undo();
        Assert.Single(layer.Shapes);
    }

    [Fact]
    public void ADot_IsNotErasedFromFarAway()
    {
        var context = new EditorContext();
        var layer = new Layer { Name = "Layer 1" };
        context.Document.Layers.Add(layer);
        context.SelectedLayer = layer;
        InkEditing.Commit(context, [new SKPoint(100, 100)], 1);

        Assert.Equal(0, InkEditing.EraseAt(context, 160, 100, 1, []));
        Assert.Single(layer.Shapes);
    }
}
