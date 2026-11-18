using Icono.Core;
using Icono.Editor.Undo;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>The Region Color tool (the bucket), shared by the Windows program and the tablet app: the front-most shape whose real outline holds the
/// place gets the current colour; a shape with curated parts (the traffic cone) falls apart into its parts first and only the tapped part is
/// coloured; the squares of a design grid are painted one by one (see <see cref="FindGridCell"/>).</summary>
public static class RegionColoring
{
    /// <summary>The design grid under the place and its square, or null. A locked grid or layer gives nothing.</summary>
    public static (Shape Grid, Layer Layer, int Row, int Col)? FindGridCell(EditorContext context, double x, double y)
    {
        foreach (var layer in context.Document.Layers.Where(l => l.Visible && !l.Locked))
        {
            foreach (var shape in layer.Shapes.Where(s => s.Kind == ShapeKind.DesignGrid && !s.Locked))
            {
                if (DesignGridMath.TryGetCell(shape, x, y, out var row, out var col))
                {
                    return (shape, layer, row, col);
                }
            }
        }

        return null;
    }

    /// <summary>Colours the shape at the place with <see cref="EditorContext.CurrentFillColor"/>. True when something was coloured.</summary>
    public static bool Apply(EditorContext context, double x, double y)
    {
        foreach (var layer in context.Document.Layers.Where(l => l.Visible))
        {
            foreach (var shape in layer.Shapes)
            {
                if (shape.Kind == ShapeKind.DesignGrid || x < shape.X || x > shape.X + shape.Width || y < shape.Y || y > shape.Y + shape.Height)
                {
                    continue;
                }

                if (shape.Kind is ShapeKind.Box3D or ShapeKind.RasterImage or ShapeKind.Text)
                {
                    return false;
                }

                if (shape.Kind != ShapeKind.CombinedPath && ShapeGeometryFactory.GetColorableRegions(shape.Kind) is { } regions)
                {
                    using var whole = ShapeGeometryFactory.Build(shape);
                    if (!whole.Contains((float)x, (float)y))
                    {
                        continue;
                    }

                    return !shape.Locked && !layer.Locked && ExplodeIntoRegions(context, shape, layer, regions, x, y);
                }

                using var built = ShapeGeometryFactory.Build(shape);
                if (!built.Contains((float)x, (float)y))
                {
                    // the frame matched but not the real outline: a piece below it may still match
                    continue;
                }

                if (shape.Locked || layer.Locked)
                {
                    return false;
                }

                var before = ShapeSnapshot.Capture(shape);
                shape.FillColor = context.CurrentFillColor;
                shape.FillEnabled = true;
                if (shape.Kind == ShapeKind.CombinedPath)
                {
                    shape.CustomPathStrokeOnly = false;
                }

                context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [ShapeSnapshot.Capture(shape)]));
                context.SelectedShape = shape;
                context.SelectedLayer = layer;
                context.RequestRedraw();
                return true;
            }
        }

        return false;
    }

    /// <summary>A not yet split shape becomes one combined path per part (same place, turn and border), and the tapped part - the smallest one that
    /// holds the place - takes the colour.</summary>
    private static bool ExplodeIntoRegions(EditorContext context, Shape shape, Layer layer, IReadOnlyList<string> regionPathData, double x, double y)
    {
        var originalIndex = layer.Shapes.IndexOf(shape);
        if (originalIndex < 0)
        {
            return false;
        }

        Shape Piece(int i) => new()
        {
            Kind = ShapeKind.CombinedPath, X = shape.X, Y = shape.Y, Width = shape.Width, Height = shape.Height, Rotation = shape.Rotation,
            FlipHorizontal = shape.FlipHorizontal, FlipVertical = shape.FlipVertical, CustomPathData = regionPathData[i],
        };

        var hitIndex = 0;
        var hitArea = float.MaxValue;
        for (var i = 0; i < regionPathData.Count; i++)
        {
            using var path = ShapeGeometryFactory.Build(Piece(i));
            if (!path.Contains((float)x, (float)y))
            {
                continue;
            }

            var area = path.Bounds.Width * path.Bounds.Height;
            if (area < hitArea)
            {
                hitArea = area;
                hitIndex = i;
            }
        }

        layer.Shapes.RemoveAt(originalIndex);
        var actions = new List<UndoableAction> { UndoableAction.ForRemove([(layer.Shapes, shape, originalIndex)]) };
        var pieces = new List<Shape>();
        for (var i = 0; i < regionPathData.Count; i++)
        {
            var piece = Piece(i);
            piece.Opacity = shape.Opacity;
            piece.FillColor = i == hitIndex ? context.CurrentFillColor : shape.FillColor;
            piece.FillEnabled = true;
            piece.BorderColor = shape.BorderColor;
            piece.BorderThickness = shape.BorderThickness;
            piece.CustomPathStrokeOnly = false;
            pieces.Add(piece);
            layer.Shapes.Insert(originalIndex + i, piece);
        }

        for (var i = 0; i < pieces.Count; i++)
        {
            actions.Add(UndoableAction.ForInsert(layer.Shapes, pieces[i], originalIndex + i));
        }

        context.UndoManager.Push(UndoableAction.Combine(actions));
        context.SelectedShape = pieces[hitIndex];
        context.SelectedLayer = layer;
        context.RequestRedraw();
        return true;
    }
}
