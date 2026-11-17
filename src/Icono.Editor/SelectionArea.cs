using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Editor;

/// <summary>Choosing objects by an area (the "marquee") - the same rule on Windows and on the tablet, as in AutoCAD: an area dragged from left to
/// right is a WINDOW (only what lies completely inside is chosen), from right to left a CROSSING (everything the area touches is chosen).</summary>
public static class SelectionArea
{
    /// <summary>True when the area was dragged from right to left (a crossing).</summary>
    public static bool IsCrossing(double startX, double currentX) => currentX < startX;

    /// <summary>The objects of the visible layers that the area chooses (front-most first).</summary>
    public static List<Shape> Find(Document document, SKRect area, bool crossing)
    {
        var found = new List<Shape>();
        foreach (var layer in document.Layers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            foreach (var shape in layer.Shapes)
            {
                if (InArea(shape, area, crossing))
                {
                    found.Add(shape);
                }
            }
        }

        return found;
    }

    public static bool InArea(Shape shape, SKRect area, bool crossing)
    {
        var bounds = Bounds(shape);
        return crossing
            ? bounds.Left <= area.Right && bounds.Right >= area.Left && bounds.Top <= area.Bottom && bounds.Bottom >= area.Top
            : bounds.Left >= area.Left && bounds.Right <= area.Right && bounds.Top >= area.Top && bounds.Bottom <= area.Bottom;
    }

    /// <summary>Where the shape really is on the page (its drawn outline, rotation included), falling back to its frame.</summary>
    public static SKRect Bounds(Shape shape)
    {
        try
        {
            using var path = ShapeGeometryFactory.Build(shape);
            var bounds = path.TightBounds;
            if (bounds.Width > 0 || bounds.Height > 0)
            {
                return bounds;
            }
        }
        catch (Exception)
        {
            // a shape without drawable geometry: use its frame below
        }

        return SKRect.Create((float)shape.X, (float)shape.Y, (float)shape.Width, (float)shape.Height);
    }
}
