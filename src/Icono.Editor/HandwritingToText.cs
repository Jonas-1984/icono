using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>Handwriting to text: the strokes of the pen (or finger) that make a word are handed to a recognizer (the platform's - on the tablet Google
/// ML Kit) and replaced by a text object in a font, at the place and about the size they had. One undo step.</summary>
public static class HandwritingToText
{
    /// <summary>The strokes to convert: the handwriting that is selected, else the strokes written since the last conversion that are still there.
    /// In the order they were written (the oldest first), which the recognizer needs.</summary>
    public static IReadOnlyList<Shape> Strokes(EditorContext context)
    {
        var chosen = context.SelectedShapes.Where(s => s.Freehand && !s.Locked).ToList();
        var pool = chosen.Count > 0 ? chosen : context.RecentInk.Where(s => s.Freehand && !s.Locked).ToList();

        // the oldest first: a layer lists its shapes front-most first, so the order of the document read backwards is the order they were written in
        var ordered = new List<Shape>();
        foreach (var layer in context.Document.Layers.Reverse())
        {
            foreach (var shape in layer.Shapes.Reverse())
            {
                if (pool.Contains(shape))
                {
                    ordered.Add(shape);
                }
            }
        }

        return ordered;
    }

    /// <summary>The strokes as lists of page points (about every 2 px along the stroke), one list per stroke.</summary>
    public static IReadOnlyList<IReadOnlyList<SKPoint>> Sample(IEnumerable<Shape> strokes)
    {
        var result = new List<IReadOnlyList<SKPoint>>();
        foreach (var stroke in strokes)
        {
            // a stroke written with pressure is an outline: the writing is read from the line down its middle (same frame, same turns)
            using var path = ShapeGeometryFactory.Build(stroke.InkCenterline is { } middle
                ? new Shape
                {
                    Kind = ShapeKind.CombinedPath, X = stroke.X, Y = stroke.Y, Width = stroke.Width, Height = stroke.Height, Rotation = stroke.Rotation,
                    FlipHorizontal = stroke.FlipHorizontal, FlipVertical = stroke.FlipVertical, CustomPathData = middle, CustomPathStrokeOnly = true,
                }
                : stroke);
            using var measure = new SKPathMeasure(path, false);
            do
            {
                var points = new List<SKPoint>();
                var length = measure.Length;
                for (float d = 0; d < length; d += 2)
                {
                    if (measure.GetPosition(d, out var p))
                    {
                        points.Add(p);
                    }
                }

                if (measure.GetPosition(length, out var end))
                {
                    points.Add(end);
                }

                if (points.Count == 0)
                {
                    // a dot (the dot of an i): a point in the middle of its frame
                    var centre = new SKPoint((float)(stroke.X + stroke.Width / 2), (float)(stroke.Y + stroke.Height / 2));
                    points.Add(centre);
                    points.Add(new SKPoint(centre.X + 0.5f, centre.Y + 0.5f));
                }

                result.Add(points);
            }
            while (measure.NextContour());
        }

        return result;
    }

    /// <summary>The font size for <paramref name="text"/> so that it fills about the place of the handwriting (width), without getting taller than
    /// the writing was.</summary>
    public static double FitFontSize(string text, double width, double height)
    {
        var lines = text.Split('\n');
        using var font = new SKFont(FontCache.Get("Segoe UI", false, false), 100);
        using var paint = new SKPaint();
        var widest = lines.Max(l =>
        {
            font.MeasureText(l, out var bounds, paint);
            return Math.Max(1, bounds.Width);
        });
        var byWidth = 100 * width / widest;
        var byHeight = height / lines.Length * 1.0;
        return Math.Clamp(Math.Min(byWidth, byHeight * 1.15), 8, 400);
    }

    /// <summary>Replaces the strokes by a text object with <paramref name="text"/>: in the colour of the pen it was written with, centred where the
    /// writing was, on the layer of its first stroke, selected. False when there is nothing to do.</summary>
    public static bool Replace(EditorContext context, IReadOnlyList<Shape> strokes, string? text)
    {
        text = text?.Trim();
        if (strokes.Count == 0 || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var left = strokes.Min(s => s.X);
        var top = strokes.Min(s => s.Y);
        var right = strokes.Max(s => s.X + s.Width);
        var bottom = strokes.Max(s => s.Y + s.Height);
        var fontSize = FitFontSize(text, right - left, bottom - top);
        var (width, height) = TextEditing.MeasureText(text, fontSize, false, false);
        var centreX = (left + right) / 2;
        var centreY = (top + bottom) / 2;

        var firstLayer = context.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(strokes[0]));
        if (firstLayer is null)
        {
            return false;
        }

        // the strokes go (one removal for each, in the order of their places), the text comes to the front of the layer
        var removals = new List<(System.Collections.ObjectModel.ObservableCollection<Shape> Collection, Shape Shape, int Index)>();
        foreach (var layer in context.Document.Layers)
        {
            foreach (var stroke in strokes.Where(s => layer.Shapes.Contains(s)))
            {
                removals.Add((layer.Shapes, stroke, layer.Shapes.IndexOf(stroke)));
            }
        }

        removals = removals.OrderBy(r => context.Document.Layers.ToList().FindIndex(l => ReferenceEquals(l.Shapes, r.Collection))).ThenBy(r => r.Index).ToList();
        foreach (var (collection, shape, _) in removals)
        {
            collection.Remove(shape);
            context.SelectedShapes.Remove(shape);
            context.RecentInk.Remove(shape);
        }

        var textShape = new Shape
        {
            Kind = ShapeKind.Text,
            X = centreX - width / 2,
            Y = centreY - height / 2,
            Width = width,
            Height = height,
            FillColor = strokes[0].BorderColor,
            BorderThickness = 0,
            TextContent = text,
            TextFontSize = fontSize,
        };
        firstLayer.Shapes.Insert(0, textShape);

        context.UndoManager.Push(UndoableAction.Combine(
        [
            UndoableAction.ForRemove(removals),
            UndoableAction.ForInsert(firstLayer.Shapes, textShape, 0),
        ]));
        context.SelectedShape = textShape;
        context.SelectedLayer = firstLayer;
        context.ForgetInk();
        context.RequestRedraw();
        return true;
    }
}
