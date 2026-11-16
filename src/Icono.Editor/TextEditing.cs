using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>What happens when typed text is kept - shared by the Windows program and the tablet app: a text object is created, changed or (left
/// empty) removed; the text inside a shape (its label) is set or removed. Each is one undo step.</summary>
public static class TextEditing
{
    /// <summary>The font size a new text object starts with (page pixels).</summary>
    public const double DefaultFontSize = 24;

    /// <summary>The frame a text object needs for <paramref name="text"/>.</summary>
    public static (double Width, double Height) MeasureText(string text, double fontSize, bool bold, bool italic)
    {
        using var font = new SKFont(FontCache.Get("Segoe UI", bold, italic), (float)fontSize);
        using var measurePaint = new SKPaint();
        font.MeasureText(text, out var bounds, measurePaint);
        return (Math.Max(20, bounds.Width + 8), Math.Max(20, bounds.Height + 8));
    }

    /// <summary>Keeps the text typed for a text object: <paramref name="existing"/> gets the new text (or is removed when it is empty), or a new
    /// text object is put at (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="layer"/>, selected, and the tool goes back to
    /// Select (as after placing any shape).</summary>
    public static void CommitTextObject(EditorContext context, Shape? existing, Layer? layer, double x, double y, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (existing is not null && layer is not null && layer.Shapes.IndexOf(existing) is var index and >= 0)
            {
                layer.Shapes.RemoveAt(index);
                context.UndoManager.Push(UndoableAction.ForRemove([(layer.Shapes, existing, index)]));
                context.SelectedShapes.Remove(existing);
            }

            context.RequestRedraw();
            return;
        }

        if (layer is null)
        {
            return;
        }

        var (width, height) = MeasureText(
            text, existing?.TextFontSize ?? DefaultFontSize, existing?.TextBold ?? false, existing?.TextItalic ?? false);

        if (existing is not null)
        {
            var before = ShapeSnapshot.Capture(existing);
            existing.TextContent = text;
            existing.Width = width;
            existing.Height = height;
            var after = ShapeSnapshot.Capture(existing);
            if (!after.Equals(before))
            {
                context.UndoManager.Push(UndoableAction.ForPropertyEdit([existing], [before], [after]));
            }
        }
        else
        {
            var shape = new Shape
            {
                Kind = ShapeKind.Text,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                FillColor = context.CurrentFillColor,
                // Text has no outline until one is added.
                BorderThickness = 0,
                TextContent = text,
            };

            layer.Shapes.Insert(0, shape);
            context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
            context.SelectedShape = shape;
            context.SelectedLayer = layer;
            context.ActiveTool = ActiveTool.Select;
        }

        context.RequestRedraw();
    }

    /// <summary>Sets the text written in a shape (an empty text removes it); the first text gets a size that fits the shape.</summary>
    public static void CommitLabel(EditorContext context, Shape shape, string? text)
    {
        text = text?.TrimEnd();
        var before = ShapeSnapshot.Capture(shape);
        var hadText = !string.IsNullOrEmpty(shape.TextContent);
        shape.TextContent = string.IsNullOrWhiteSpace(text) ? null : text;
        if (!hadText && shape.TextContent is not null)
        {
            shape.TextFontSize = ShapeLabel.SuggestedFontSize(shape);
        }

        var after = ShapeSnapshot.Capture(shape);
        if (!after.Equals(before))
        {
            context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [after]));
        }

        context.RequestRedraw();
    }
}
