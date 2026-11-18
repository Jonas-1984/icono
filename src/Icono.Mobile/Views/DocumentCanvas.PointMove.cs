using Avalonia;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>Snapping while points and objects are moved: a dragged corner or end point, a dragged handle and a moved object put themselves on the points of other
/// objects (ends, middles, centres, crossings ...), as the Windows program does. The kinds are chosen in the Snap tab; a mark shows what was found.</summary>
public sealed partial class DocumentCanvas
{
    /// <summary>The place a dragged point goes to: on a point of another object (the shape itself is left out), or <paramref name="at"/> as it is. The neighbour
    /// of the point lets the right-angle and parallel kinds work.</summary>
    private Point SnapDraggedPoint(Point at, IReadOnlyCollection<Shape> own, SKPoint? neighbour = null) =>
        SnapToObjectPoints(at, own, neighbour) ?? at;

    /// <summary>A selection that is moved brings its own points (corners, middles, centres ...) and puts them on the points of the other objects: a cabinet
    /// corner on a wall corner. True when it snapped (the selection has been moved by the amount needed).</summary>
    private bool SnapSelectionToPoints()
    {
        _snapMark = null;
        _snapDetail = null;
        _wallSnapResult = null;
        if (_context is not { } context || !context.PointSnap.Enabled || _editShapes.Count == 0 || _editShapes.Count > 8)
        {
            return false;
        }

        var own = PointSnapper.PointsOf(_editShapes, context.PointSnap);
        if (own.Count == 0)
        {
            return false;
        }

        var tolerance = (float)(FingerReach / _zoom);
        var area = new SKRect(
            own.Min(p => p.Point.X) - tolerance, own.Min(p => p.Point.Y) - tolerance, own.Max(p => p.Point.X) + tolerance, own.Max(p => p.Point.Y) + tolerance);
        var targets = PointSnapper.FixedPoints(context.Document, area, context.PointSnap, _editShapes);

        var best = double.MaxValue;
        (SKPoint Own, SKPoint Target, PointSnapKind Kind)? hit = null;
        foreach (var (target, kind) in targets)
        {
            foreach (var (mine, _) in own)
            {
                var distance = Math.Sqrt((mine.X - target.X) * (mine.X - target.X) + (mine.Y - target.Y) * (mine.Y - target.Y));
                if (distance <= tolerance && distance < best)
                {
                    best = distance;
                    hit = (mine, target, kind);
                }
            }
        }

        if (hit is not { } found)
        {
            return false;
        }

        var dx = found.Target.X - found.Own.X;
        var dy = found.Target.Y - found.Own.Y;
        foreach (var shape in _editShapes)
        {
            shape.X += dx;
            shape.Y += dy;
        }

        var result = new PointSnapResult { Point = found.Target, Kind = found.Kind };
        _snapMark = (found.Target.X, found.Target.Y, result.Label);
        _snapDetail = result;
        return true;
    }

    /// <summary>The marks of a snap that was shown while editing go when the finger lifts.</summary>
    private void ForgetSnapMark()
    {
        _snapMark = null;
        _snapDetail = null;
        _wallSnapResult = null;
    }
}
