using System.Windows;
using System.Windows.Input;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>Snapping to points of the objects (end points, middles, centres, crossings ...) for every tool: drawing, placing, dragging a
/// corner, resizing and moving.</summary>
public partial class CanvasHostView
{
    /// <summary>The snap found for the pointer (kept for the mark that is drawn there).</summary>
    private PointSnapResult? _pointSnap;

    /// <summary>The point snapped to the objects around it, or null when nothing is near (or Alt holds the snapping off). Remembers the snap for
    /// the mark. <paramref name="reference"/> is the last point of what is being drawn (perpendicular, tangent, parallel need it).</summary>
    private Point? SnapToPoints(Point raw, SKPoint? reference = null, IReadOnlyCollection<Shape>? exclude = null)
    {
        if (_context is null || WallSnappingSuspended)
        {
            _pointSnap = null;
            return null;
        }

        _pointSnap = PointSnapper.Snap(
            _context.Document, new SKPoint((float)raw.X, (float)raw.Y), AnchorSnapDip / Zoom, _context.PointSnap, exclude ?? [], reference);
        return _pointSnap is null ? null : new Point(_pointSnap.Point.X, _pointSnap.Point.Y);
    }

    /// <summary>The last point of what is being drawn or dragged, from which the perpendicular, tangent and parallel snaps are found.</summary>
    private SKPoint? PointSnapReference()
    {
        static SKPoint At(Shape shape, CorePoint p) => new((float)(shape.X + p.X), (float)(shape.Y + p.Y));

        if (_isPathDrawing && _pendingPathShape is { Points: { Count: >= 2 } path } pending)
        {
            return At(pending, path[^2]);
        }

        if (_isPlacing && _pendingShape?.Points is not null)
        {
            return new SKPoint((float)_placeStart.X, (float)_placeStart.Y);
        }

        if (_isDraggingPoint && _pointEditShape is { Points: { Count: >= 2 } points } edited && _draggedPointIndex is { } index)
        {
            return At(edited, points[index > 0 ? index - 1 : index + 1]);
        }

        return null;
    }

    /// <summary>The snap mark follows the pointer while a tool that places points is active, even before anything is drawn.</summary>
    private void UpdatePointSnapHover(Point raw)
    {
        if (_context is not { ActiveTool: ActiveTool.Shape or ActiveTool.PathDraw } || _isPlacing || _isPathDrawing || _isDraggingPoint)
        {
            return;
        }

        var before = _pointSnap;
        SnapToPoints(raw);
        if (before?.Point != _pointSnap?.Point || before?.Kind != _pointSnap?.Kind)
        {
            SkiaCanvas.InvalidateVisual();
        }
    }

    /// <summary>A selection that is moved brings its own points (corners, middles, centres ...) and puts them on the points of the other objects:
    /// a cabinet corner on a wall corner. Returns true when it snapped (the selection has been moved by the amount needed).</summary>
    private bool SnapSelectionToPoints()
    {
        _pointSnap = null;
        if (_context is null || WallSnappingSuspended || !_context.PointSnap.Enabled || _context.SelectedShapes.Count == 0 || _context.SelectedShapes.Count > 8)
        {
            return false;
        }

        var moving = _context.SelectedShapes.ToList();
        var own = PointSnapper.PointsOf(moving, _context.PointSnap);
        if (own.Count == 0)
        {
            return false;
        }

        var tolerance = AnchorSnapDip / Zoom;
        var area = new SKRect(
            own.Min(p => p.Point.X) - (float)tolerance, own.Min(p => p.Point.Y) - (float)tolerance,
            own.Max(p => p.Point.X) + (float)tolerance, own.Max(p => p.Point.Y) + (float)tolerance);
        var targets = PointSnapper.FixedPoints(_context.Document, area, _context.PointSnap, moving);

        var best = double.MaxValue;
        (SKPoint Own, SKPoint Target, PointSnapKind Kind)? hit = null;
        foreach (var (target, kind) in targets)
        {
            foreach (var (mine, _) in own)
            {
                var distance = Geo.Distance(mine, target);
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
        foreach (var shape in moving)
        {
            shape.X += dx;
            shape.Y += dy;
        }

        _pointSnap = new PointSnapResult { Point = found.Target, Kind = found.Kind };
        return true;
    }

    // ----- the mark -----------------------------------------------------------------------------------------------

    private void DrawPointSnap(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_pointSnap is not { } snap)
        {
            return;
        }

        var x = snap.Point.X * scaleX;
        var y = snap.Point.Y * scaleY;

        using var guide = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = MeasureOrange.WithAlpha(210),
            PathEffect = SKPathEffect.CreateDash([5, 4], 0),
        };
        foreach (var line in snap.Guides)
        {
            canvas.DrawLine(line.From.X * scaleX, line.From.Y * scaleY, line.To.X * scaleX, line.To.Y * scaleY, guide);
        }

        using var halo = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4.2f, Color = SKColors.White.WithAlpha(190) };
        using var mark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = MeasureOrange };
        foreach (var paint in new[] { halo, mark })
        {
            DrawSnapMark(canvas, snap.Kind, x, y, paint);
        }

        DrawPill(canvas, snap.Label, x + 14, y - 30);
    }

    private static void DrawSnapMark(SKCanvas canvas, PointSnapKind kind, float x, float y, SKPaint paint)
    {
        const float h = 6f;
        switch (kind)
        {
            case PointSnapKind.Endpoint:
                canvas.DrawRect(x - h, y - h, 2 * h, 2 * h, paint);
                break;
            case PointSnapKind.Midpoint:
            {
                using var triangle = new SKPath();
                triangle.MoveTo(x, y - 7);
                triangle.LineTo(x + 7, y + 6);
                triangle.LineTo(x - 7, y + 6);
                triangle.Close();
                canvas.DrawPath(triangle, paint);
                break;
            }

            case PointSnapKind.Center:
                canvas.DrawCircle(x, y, h, paint);
                break;
            case PointSnapKind.GeometricCenter:
                canvas.DrawCircle(x, y, h, paint);
                canvas.DrawLine(x - h, y, x + h, y, paint);
                canvas.DrawLine(x, y - h, x, y + h, paint);
                break;
            case PointSnapKind.Node:
                canvas.DrawCircle(x, y, h - 1, paint);
                canvas.DrawLine(x - 3.5f, y - 3.5f, x + 3.5f, y + 3.5f, paint);
                canvas.DrawLine(x - 3.5f, y + 3.5f, x + 3.5f, y - 3.5f, paint);
                break;
            case PointSnapKind.Quadrant:
            {
                using var diamond = new SKPath();
                diamond.MoveTo(x, y - 7);
                diamond.LineTo(x + 7, y);
                diamond.LineTo(x, y + 7);
                diamond.LineTo(x - 7, y);
                diamond.Close();
                canvas.DrawPath(diamond, paint);
                break;
            }

            case PointSnapKind.Intersection:
                canvas.DrawLine(x - h, y - h, x + h, y + h, paint);
                canvas.DrawLine(x - h, y + h, x + h, y - h, paint);
                break;
            case PointSnapKind.Extension:
                canvas.DrawLine(x - 7, y, x - 4, y, paint);
                canvas.DrawLine(x - 1.5f, y, x + 1.5f, y, paint);
                canvas.DrawLine(x + 4, y, x + 7, y, paint);
                break;
            case PointSnapKind.Insertion:
                canvas.DrawRect(x - h, y - h, 2 * h, 2 * h, paint);
                canvas.DrawLine(x - h, y, x + h, y, paint);
                canvas.DrawLine(x, y - h, x, y + h, paint);
                break;
            case PointSnapKind.Perpendicular:
                canvas.DrawLine(x - h, y - h, x - h, y + h, paint);
                canvas.DrawLine(x - h, y + h, x + h, y + h, paint);
                canvas.DrawRect(x - h, y + h - 5, 5, 5, paint);
                break;
            case PointSnapKind.Tangent:
                canvas.DrawCircle(x, y - 1, 5, paint);
                canvas.DrawLine(x - 8, y + 4, x + 8, y + 4, paint);
                break;
            case PointSnapKind.Nearest:
            {
                using var hourglass = new SKPath();
                hourglass.MoveTo(x - h, y - h);
                hourglass.LineTo(x + h, y - h);
                hourglass.LineTo(x - h, y + h);
                hourglass.LineTo(x + h, y + h);
                hourglass.Close();
                canvas.DrawPath(hourglass, paint);
                break;
            }

            case PointSnapKind.ApparentIntersection:
                canvas.DrawLine(x - 4, y - 4, x + 4, y + 4, paint);
                canvas.DrawLine(x - 4, y + 4, x + 4, y - 4, paint);
                canvas.DrawRect(x - h - 1, y - h - 1, 2 * h + 2, 2 * h + 2, paint);
                break;
            default: // Parallel
                canvas.DrawLine(x - 7, y + 2, x + 1, y - 6, paint);
                canvas.DrawLine(x - 1, y + 6, x + 7, y - 2, paint);
                break;
        }
    }
}
