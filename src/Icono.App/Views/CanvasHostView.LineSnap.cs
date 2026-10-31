using System.Windows;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>The snapping tools for lines: a line or polyline that is drawn, and a point of a line that is dragged (Edit Points), snap to the
/// points of the objects, to the part of the line that is already there, and with the helpers of the wall tool (horizontal / vertical, Shift for
/// an exact axis, parallel and perpendicular, lining up with other points, equal length), then to the faces and edges of walls and shapes.</summary>
public partial class CanvasHostView
{
    /// <summary>The part of a line that is already placed as a shape of its own, so its end points, middles, crossings and edges attract the
    /// point being placed (to close a figure on its start point, to cross itself ...).</summary>
    private static Shape? OwnLine(IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var line = new Shape { Kind = ShapeKind.Polyline, Points = points.Select(p => new CorePoint(p.X, p.Y)).ToList() };
        PathBoundsCalculator.Recompute(line);
        return line;
    }

    /// <summary>The page position for a point of a line: null when nothing snapped (the caller then falls back to the grid).</summary>
    /// <param name="own">The other points of the line (without the moving one), in page px.</param>
    /// <param name="previous">The point the moving one is joined to: directions and lengths are measured from it.</param>
    /// <param name="edited">The shape whose point is dragged (it does not attract itself), null while drawing.</param>
    private Point? SnapLinePoint(Point raw, IReadOnlyList<SKPoint> own, SKPoint? previous, Shape? edited)
    {
        _wallSnap = null;
        _snapGuides = [];
        if (_context is null)
        {
            return null;
        }

        var ownLine = OwnLine(own);
        IReadOnlyCollection<Shape> excluded = edited is null ? [] : [edited];
        if (SnapToPoints(raw, previous, excluded, ownLine is null ? null : [ownLine]) is { } onPoint)
        {
            return onPoint;
        }

        var wall = RunWallSnap(raw, own, edited, forLine: true, previous);
        if (wall.Snapped)
        {
            _wallSnap = wall;
            return new Point(wall.Point.X, wall.Point.Y);
        }

        var frame = SnapFrame((raw.X, raw.Y, 0, 0), excluded);
        return frame.Snapped ? new Point(raw.X + frame.Dx, raw.Y + frame.Dy) : null;
    }

    /// <summary>The other points of a line whose point <paramref name="index"/> is dragged, and the point it hangs on (its neighbour).</summary>
    private static (List<SKPoint> Others, SKPoint? Previous) OtherPoints(Shape shape, int index)
    {
        var all = WallGeometry.AbsolutePoints(shape);
        SKPoint? previous = all.Count >= 2 ? all[index > 0 ? index - 1 : index + 1] : null;
        all.RemoveAt(index);
        return (all, previous);
    }
}
