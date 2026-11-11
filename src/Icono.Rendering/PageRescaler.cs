using Icono.Core;

namespace Icono.Rendering;

/// <summary>What a change of the drawing scale does to the drawing. The scale belongs to the PAGE: it says how many real-world cm one cm of the page
/// stands for. When it changes from 1:40 to 1:50 the page now stands for more, so everything that is a real-world thing (rooms, walls, doors,
/// furniture) has to become smaller on the page - then its real-world size, and with it every dimension, stays what it was.
/// What belongs to the paper stays as it is: the weight of the lines, the size of the text, the size of arrowheads and the distance of a
/// dimension line from what it measures. Everything is scaled about the middle of the page it lies on, so the drawing stays where it is
/// on the sheet.</summary>
public static class PageRescaler
{
    /// <summary>The factor for the page lengths when the scale 1:<paramref name="from"/> becomes 1:<paramref name="to"/>.</summary>
    public static double Factor(double from, double to) => from / to;

    /// <summary>Resizes the whole drawing (every layer, also hidden and locked ones) by <paramref name="factor"/>: see the class description.</summary>
    public static void Rescale(Document document, double factor)
    {
        if (factor <= 0 || Math.Abs(factor - 1) < 1e-12)
        {
            return;
        }

        foreach (var layer in document.Layers)
        {
            foreach (var shape in layer.Shapes)
            {
                Rescale(document, shape, factor);
            }
        }
    }

    private static void Rescale(Document document, Shape shape, double factor)
    {
        // a design grid is the grid of an icon (its squares are pixels of the icon), not something at a scale
        if (shape.Kind == ShapeKind.DesignGrid)
        {
            return;
        }

        var page = document.PageIndexAt(shape.Y + shape.Height / 2);
        var centreX = document.Width / 2.0;
        var centreY = document.PageTop(page) + document.Height / 2.0;

        double Across(double value, double centre) => centre + (value - centre) * factor;

        if (shape.Kind == ShapeKind.Text)
        {
            // a text keeps its size (it is written on the paper); its place on the page follows the drawing
            var middleX = Across(shape.X + shape.Width / 2, centreX);
            var middleY = Across(shape.Y + shape.Height / 2, centreY);
            shape.X = middleX - shape.Width / 2;
            shape.Y = middleY - shape.Height / 2;
            return;
        }

        var newX = Across(shape.X, centreX);
        var newY = Across(shape.Y, centreY);
        if (shape.Points is { Count: > 0 } points)
        {
            // points are relative to the corner of the frame: the frame's corner moves, the distances between the points scale
            shape.Points = points.Select(p => new CorePoint(p.X * factor, p.Y * factor)).ToList();
            shape.X = newX;
            shape.Y = newY;
            shape.Width *= factor;
            shape.Height *= factor;
        }
        else
        {
            // the frame is scaled about the middle of the page: its middle moves, its size scales
            var middleX = Across(shape.X + shape.Width / 2, centreX);
            var middleY = Across(shape.Y + shape.Height / 2, centreY);
            shape.Width *= factor;
            shape.Height *= factor;
            shape.X = middleX - shape.Width / 2;
            shape.Y = middleY - shape.Height / 2;
        }

        // the lengths that are real-world sizes: the thickness of a wall (and of the wall a door or window sits in), rounded corners and
        // the radii of single corners, the depth of a box
        shape.WallThickness *= factor;
        shape.CornerRadius *= factor;
        shape.Depth3D *= factor;
        if (shape.PointRadii is { Count: > 0 } radii)
        {
            shape.PointRadii = radii.Select(r => r * factor).ToList();
        }
    }
}
