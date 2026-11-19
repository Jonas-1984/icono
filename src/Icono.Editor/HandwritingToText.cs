using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>Handwriting to text: the strokes of the pen (or finger) that make a word are handed to a recognizer (the platform's - on the tablet Google
/// ML Kit) and replaced by a text object in a font, at the place and about the size they had. One undo step.</summary>
public static class HandwritingToText
{
    /// <summary>How far apart (as a part of the height of the taller stroke) two strokes of one writing may be: the letters of a word, and the words of a line, are
    /// close; a drawing beside it is further away.</summary>
    private const double GapFactor = 1.0;

    /// <summary>The least gap (drawing units) that strokes still belong together - the dot of an i, a stroke of a tiny letter.</summary>
    private const double MinGap = 6;

    /// <summary>The strokes to convert now: the handwriting that is selected (what the user chose, as it is), else ONE group of the strokes written since the last
    /// conversion - the group written last, or another one picked with <see cref="EditorContext.SetInkGroup"/> - never all of them: drawings made with the same pen
    /// stay what they are. In the order they were written (the oldest first), which the recognizer needs.</summary>
    public static IReadOnlyList<Shape> Strokes(EditorContext context)
    {
        var chosen = context.SelectedShapes.Where(s => s.Freehand && !s.Locked).ToList();
        if (chosen.Count > 0)
        {
            return Ordered(context, chosen);
        }

        var groups = RecentGroups(context);
        if (groups.Count == 0)
        {
            return [];
        }

        return groups[groups.Count - 1 - Math.Clamp(context.InkGroupIndex, 0, groups.Count - 1)];
    }

    /// <summary>True when the strokes that are offered are the ones the user selected (not the recent writing).</summary>
    public static bool IsSelection(EditorContext context) => context.SelectedShapes.Any(s => s.Freehand && !s.Locked);

    /// <summary>The recent writing as groups of strokes that lie together (a word, a line), the one written longest ago first and the one touched last at the end.</summary>
    public static IReadOnlyList<IReadOnlyList<Shape>> RecentGroups(EditorContext context) =>
        Groups(Ordered(context, context.RecentInk.Where(s => s.Freehand && !s.Locked).ToList()));

    /// <summary>The strokes (in the order they were written) as groups: strokes that are near each other - the gap between their frames is no more than about a
    /// letter height - belong together, directly or through others. The groups are ordered by the last stroke they have, so the group written last is the last one.</summary>
    public static IReadOnlyList<IReadOnlyList<Shape>> Groups(IReadOnlyList<Shape> strokes)
    {
        var parent = Enumerable.Range(0, strokes.Count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        for (var i = 0; i < strokes.Count; i++)
        {
            for (var j = i + 1; j < strokes.Count; j++)
            {
                if (Near(strokes[i], strokes[j]))
                {
                    parent[Find(j)] = Find(i);
                }
            }
        }

        return Enumerable.Range(0, strokes.Count)
            .GroupBy(Find)
            .OrderBy(group => group.Max())
            .Select(group => (IReadOnlyList<Shape>)group.Select(i => strokes[i]).ToList())
            .ToList();
    }

    private static bool Near(Shape a, Shape b)
    {
        var dx = Math.Max(0, Math.Max(a.X, b.X) - Math.Min(a.X + a.Width, b.X + b.Width));
        var dy = Math.Max(0, Math.Max(a.Y, b.Y) - Math.Min(a.Y + a.Height, b.Y + b.Height));
        var reach = Math.Max(MinGap, GapFactor * Math.Max(a.Height, b.Height));
        return Math.Sqrt(dx * dx + dy * dy) <= reach;
    }

    /// <summary>The strokes in the order they were written: a layer lists its shapes front-most first, so the document read backwards is the order of writing.</summary>
    private static List<Shape> Ordered(EditorContext context, IReadOnlyCollection<Shape> pool)
    {
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
        context.InkListChanged(); // the other writing that is left stays on offer
        context.RequestRedraw();
        return true;
    }
}
