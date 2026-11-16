using Icono.Core;

namespace Icono.Rendering;

/// <summary>Which object lies under a point of the drawing - the same rules for the Windows program and the tablet app.</summary>
public static class ShapeHitTester
{
    /// <summary>The front-most object of a visible layer under (<paramref name="x"/>, <paramref name="y"/>) in drawing units, with its layer, or
    /// null. <paramref name="zoom"/> is screen pixels per drawing unit; <paramref name="reachPx"/> is how far beside thin objects (lines of a
    /// dimension, walls, doors, handwriting) a pointer still counts, in screen pixels - a finger needs more than a mouse.</summary>
    public static (Shape Shape, Layer Layer)? Find(Document document, double x, double y, double zoom, double reachPx = 4)
    {
        var reach = reachPx / Math.Max(zoom, 1e-6);

        // Front-to-back: index 0 = front-most, in both layer and shape lists.
        foreach (var layer in document.Layers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            foreach (var shape in layer.Shapes)
            {
                if (Hits(shape, x, y, reach))
                {
                    return (shape, layer);
                }
            }
        }

        return null;
    }

    private static bool Hits(Shape shape, double x, double y, double reach)
    {
        if (shape.Kind == ShapeKind.DesignGrid)
        {
            // The cell lookup undoes rotation and flips, so a rotated grid is hit exactly where it is drawn.
            return DesignGridMath.TryGetCell(shape, x, y, out _, out _);
        }

        if (shape.Kind == ShapeKind.Dimension)
        {
            // Only its lines, arrowheads and the written value count - not the empty area between them.
            return DimensionRenderer.HitTest(shape, x, y, reach);
        }

        if (shape.Freehand)
        {
            // a stroke written by hand is picked where it is drawn, not anywhere in its frame
            return FreehandBuilder.HitTest(shape, x, y, reach * 1.25);
        }

        if (shape.Kind == ShapeKind.Wall)
        {
            // A wall is hit on its band (not on the empty inside of a room or the corner of its bounding box).
            return WallEditor.Nearest(shape, x, y, 0) is { } onWall && onWall.Distance <= shape.WallThickness / 2 + reach * 0.75;
        }

        if (ArchitecturalSymbols.IsSymbol(shape.Kind))
        {
            // a door or window is thin and often turned: it is hit where it is drawn, a few pixels around it included
            return ArchitecturalSymbols.HitTest(shape, x, y, reach * 1.25);
        }

        // a turned shape is hit in its own (unturned) frame
        var (localX, localY) = shape.Rotation == 0 ? (x, y) : Unrotate(shape, x, y);
        return localX >= shape.X && localX <= shape.X + shape.Width && localY >= shape.Y && localY <= shape.Y + shape.Height;
    }

    private static (double X, double Y) Unrotate(Shape shape, double x, double y)
    {
        var cx = shape.X + shape.Width / 2;
        var cy = shape.Y + shape.Height / 2;
        var rad = -shape.Rotation * Math.PI / 180.0;
        var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
        var (dx, dy) = (x - cx, y - cy);
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }
}
