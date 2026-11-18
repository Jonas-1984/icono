using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>Makes the object of a recognized stroke (<see cref="StrokeShapeRecognizer"/>): a real line, rectangle or ellipse, or a closed polyline for a
/// triangle or any other corners - in the pen colour and width, without fill - and keeps it as one undo step.</summary>
public static class StrokeShapes
{
    public static Shape Build(RecognizedStroke shape, ArgbColor colour, double width)
    {
        Shape result;
        switch (shape.Kind)
        {
            case RecognizedKind.Line:
                result = ShapeTemplates.NewTwoPointLine(ShapeKind.Line, shape.Points[0].X, shape.Points[0].Y, colour);
                result.Points = [new CorePoint(0, 0), new CorePoint(shape.Points[1].X - shape.Points[0].X, shape.Points[1].Y - shape.Points[0].Y)];
                PathBoundsCalculator.Recompute(result);
                break;

            case RecognizedKind.Rectangle or RecognizedKind.Ellipse:
                result = ShapeTemplates.NewBox(
                    shape.Kind == RecognizedKind.Rectangle ? ShapeKind.Rectangle : ShapeKind.Ellipse,
                    shape.Center.X - shape.Width / 2, shape.Center.Y - shape.Height / 2, colour);
                result.Width = shape.Width;
                result.Height = shape.Height;
                result.Rotation = shape.Angle;
                break;

            default:
            {
                // corners: a closed polyline (the first corner again at the end)
                var first = shape.Points[0];
                result = new Shape
                {
                    Kind = ShapeKind.Polyline,
                    X = first.X,
                    Y = first.Y,
                    Width = 1,
                    Height = 1,
                    FillColor = colour,
                    Points = shape.Points.Append(first).Select(p => new CorePoint(p.X - first.X, p.Y - first.Y)).ToList(),
                };
                PathBoundsCalculator.Recompute(result);
                break;
            }
        }

        result.FillEnabled = false;
        result.BorderColor = colour;
        result.BorderThickness = width;
        return result;
    }

    /// <summary>The clean shape as it is shown while the pen is held (not part of the drawing yet).</summary>
    public static Shape Preview(EditorContext context, RecognizedStroke shape) => Build(shape, context.PenColor, context.PenWidth);

    /// <summary>The clean shape becomes an object at the front of the active (or first unlocked) layer, selected. False when no layer can take it.</summary>
    public static bool Commit(EditorContext context, RecognizedStroke shape)
    {
        var layer = context.SelectedLayer is { Locked: false } selected ? selected : context.Document.Layers.FirstOrDefault(l => !l.Locked);
        if (layer is null)
        {
            context.Notify("All layers are locked.");
            return false;
        }

        var result = Build(shape, context.PenColor, context.PenWidth);
        layer.Shapes.Insert(0, result);
        context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, result, 0));
        context.RequestRedraw();
        return true;
    }
}
