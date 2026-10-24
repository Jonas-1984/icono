using Icono.Core;

namespace Icono.Rendering;

/// <summary>
/// The box a shape is DRAWN in. A shape's X/Y/Width/Height is the frame of its template; the icon templates keep a margin
/// (and a rectangle is not square) inside that frame, so what the user sees, measures and selects is this outline box.
/// Lines, text, images and grids fill their frame, so for them both are the same.
/// </summary>
public static class ShapeOutline
{
    public static (double X, double Y, double W, double H) Box(Shape shape)
    {
        if (shape.Points is not null || shape.Kind is ShapeKind.Text or ShapeKind.RasterImage or ShapeKind.DesignGrid or ShapeKind.Box3D)
        {
            return (shape.X, shape.Y, shape.Width, shape.Height);
        }

        var box = ConnectionRouter.GetBox(shape);
        return (box.Left, box.Top, box.Width, box.Height);
    }

    /// <summary>Moves/resizes the shape so its drawn outline becomes the given box (the frame is derived from it).</summary>
    public static void SetBox(Shape shape, double x, double y, double width, double height)
    {
        if (shape.Points is { Count: > 0 } points)
        {
            // A line, polyline or wall is its points: stretch them so the whole path gets the new size.
            var sx = shape.Width > 0.0001 ? Math.Max(1, width) / shape.Width : 1;
            var sy = shape.Height > 0.0001 ? Math.Max(1, height) / shape.Height : 1;
            shape.Points = points.Select(p => new CorePoint(p.X * sx, p.Y * sy)).ToList();
            shape.X = x;
            shape.Y = y;
            shape.Width = Math.Max(1, width);
            shape.Height = Math.Max(1, height);
            PathBoundsCalculator.Recompute(shape);
            return;
        }

        var (bx, by, bw, bh) = Box(shape);
        var frameW = shape.Width;
        var frameH = shape.Height;

        var ratioW = frameW > 0.0001 && bw > 0.0001 ? bw / frameW : 1;
        var ratioH = frameH > 0.0001 && bh > 0.0001 ? bh / frameH : 1;
        var insetX = frameW > 0.0001 ? (bx - shape.X) / frameW : 0;
        var insetY = frameH > 0.0001 ? (by - shape.Y) / frameH : 0;

        var newW = Math.Max(1, width) / ratioW;
        var newH = Math.Max(1, height) / ratioH;
        shape.Width = newW;
        shape.Height = newH;
        shape.X = x - insetX * newW;
        shape.Y = y - insetY * newH;
    }
}
