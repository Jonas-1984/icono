using Avalonia;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The snapping tools for lines, polylines and polygons - a line that is drawn, and a point of a line or shape that is dragged: the point snaps to
/// the points of the other objects AND of the part of the line that is already there (to close a figure on its start, to cross itself), then to the helpers of
/// the wall tool (horizontal and vertical, parallel and at a right angle, lining up with other points, equal length), as in the Windows program.</summary>
public sealed partial class DocumentCanvas
{
    /// <summary>True for the kinds that are drawn point by point or have points that can be dragged and that take these helpers.</summary>
    private static bool IsLineLike(ShapeKind kind) =>
        kind is ShapeKind.Line or ShapeKind.Arrow or ShapeKind.DoubleArrow or ShapeKind.Polyline or ShapeKind.Curve or ShapeKind.Polygon;

    /// <summary>The part of a line that is already placed as a shape of its own, so its end points, middles and crossings attract the point being placed.</summary>
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
        _snapMark = null;
        _snapDetail = null;
        _wallSnapResult = null;
        if (_context is not { } context)
        {
            return null;
        }

        var cursor = new SKPoint((float)raw.X, (float)raw.Y);
        var reach = FingerReach / _zoom;
        var ownLine = OwnLine(own);
        IReadOnlyCollection<Shape> excluded = edited is null ? [] : [edited];

        // a point of an object, or of the rest of this line: its end points, middles, crossings, the foot of a perpendicular ...
        if (PointSnapper.Snap(context.Document, cursor, reach, context.PointSnap, excluded, previous, ownLine is null ? null : [ownLine]) is { } onPoint)
        {
            _snapMark = (onPoint.Point.X, onPoint.Point.Y, onPoint.Label);
            _snapDetail = onPoint;
            return new Point(onPoint.Point.X, onPoint.Point.Y);
        }

        // then the helpers of the wall tool: straight across / up, parallel and right angle, in line with other points, as long as the piece before
        var wall = WallSnapper.Snap(cursor, previous, own, context.Document, context.WallSnap, reach, wallThickness: 0, exclude: edited);
        if (wall.Snapped)
        {
            _snapMark = (wall.Point.X, wall.Point.Y, wall.Label);
            _snapDetail = null;
            _wallSnapResult = wall;
            return new Point(wall.Point.X, wall.Point.Y);
        }

        return null;
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
