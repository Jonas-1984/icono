using System.Windows;
using System.Windows.Input;
using Icono.App.State;
using Icono.App.State.Undo;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>Placing the dimension of a wall, a door or a window with the mouse alone, like the "dimension line location" point of AutoCAD: press
/// on the number of the dimension and drag - its line follows the pointer, on either side of the wall, at any distance - and let go where it
/// should stay. A click without dragging still opens the length editor (type the length you want).</summary>
public partial class CanvasHostView
{
    /// <summary>How far (screen px) the pointer has to travel before a press on a number is a drag and not a click.</summary>
    private const double DimensionDragPx = 4;

    private Shape? _dimensionShape;
    private int _dimensionSegment;
    private SymbolDimension? _dimensionOfSymbol;
    private ShapeSnapshot? _dimensionBefore;
    private Point _dimensionPressPoint;
    private Point _dimensionPressScreen;
    private bool _dimensionDragging;
    private SKPoint _dimensionDirection;

    /// <summary>The dimension line the one that is placed or dragged has lined up with (shown as a guide), or null.</summary>
    private (SKPoint From, SKPoint To)? _dimensionLineGuide;

    /// <summary>A press on the number of a wall or door/window dimension: remembers it. What happens (length editor or placing the line) is
    /// decided when the mouse moves or is let go. True when the point is on such a number.</summary>
    private bool TryPressDimensionNumber(Point point, Point screen)
    {
        if (FindWallDimensionNumber(point) is { } wall)
        {
            _dimensionShape = wall.Wall;
            _dimensionSegment = wall.Segment;
            _dimensionOfSymbol = null;
        }
        else if (FindSymbolDimensionNumber(point) is { } symbol)
        {
            _dimensionShape = symbol.Symbol;
            _dimensionOfSymbol = symbol.Dimension;
        }
        else
        {
            return false;
        }

        // the way the dimension line runs (the lines of other dimensions that run the same way attract it)
        var runs = _dimensionOfSymbol?.Layout ?? WallRenderer.LayoutSegmentDimension(_dimensionShape, _dimensionSegment);
        _dimensionDirection = runs is null ? default : new SKPoint(runs.LineTo.X - runs.LineFrom.X, runs.LineTo.Y - runs.LineFrom.Y);
        _dimensionBefore = ShapeSnapshot.Capture(_dimensionShape);
        _dimensionPressPoint = point;
        _dimensionPressScreen = screen;
        _dimensionDragging = false;
        CaptureMouse();
        return true;
    }

    /// <summary>The pointer moved while the number is pressed: past a few pixels the dimension line is placed where the pointer is.</summary>
    private void ContinueDimensionPress(MouseEventArgs e)
    {
        if (_context is null || _dimensionShape is not { } shape)
        {
            return;
        }

        if (!_dimensionDragging)
        {
            var screen = e.GetPosition(this);
            if (Math.Abs(screen.X - _dimensionPressScreen.X) < DimensionDragPx && Math.Abs(screen.Y - _dimensionPressScreen.Y) < DimensionDragPx)
            {
                return;
            }

            _dimensionDragging = true;
            if (shape.Kind == ShapeKind.Wall)
            {
                _context.PickedDimensionSegment = _dimensionSegment;
            }

            _context.SelectedShape = shape;
        }

        var at = SnapDimensionPointer(ToDocumentSpace(e.GetPosition(SkiaCanvas)), shape);
        var point = new SKPoint((float)at.X, (float)at.Y);
        if (shape.Kind == ShapeKind.Wall)
        {
            if (WallRenderer.DimensionLineThrough(shape, _dimensionSegment, point) is { } line)
            {
                SetWallDimensionLine(shape, _dimensionSegment, line.Offset, line.Flip);
            }
        }
        else
        {
            SymbolMeasurements.PlaceDimensionLine(_context.Document, shape, point);
        }

        _context.RequestRedraw();
    }

    /// <summary>Where the dimension line goes for the pointer: the pointer snaps like every other point that is moved - to the points of the
    /// objects (corners, middles, centres, crossings ...), failing that it lines up with the faces and edges of walls and shapes (the guides are
    /// shown), failing that it goes to the grid. Alt holds the snapping off.</summary>
    private Point SnapDimensionPointer(Point raw, Shape owner)
    {
        // the shape whose dimension is dragged does not attract the pointer: its own dimension line would pull it along and run away
        IReadOnlyCollection<Shape> excluded = [owner];
        if (SnapToDimensionLines(raw, owner, owner.Kind == ShapeKind.Wall ? _dimensionSegment : -1, _dimensionDirection) is { } inRow)
        {
            return inRow;
        }

        if (SnapToPoints(raw, null, excluded) is { } onPoint)
        {
            _snapGuides = [];
            return onPoint;
        }

        var frame = SnapFrame((raw.X, raw.Y, 0, 0), excluded);
        if (frame.Snapped)
        {
            return new Point(raw.X + frame.Dx, raw.Y + frame.Dy);
        }

        return new Point(SnapIfEnabled(raw.X), SnapIfEnabled(raw.Y));
    }

    /// <summary>Lines the dimension line up with another dimension line that runs the same way, so that two dimensions stand in one row, flush
    /// (the line it lined up with is shown). Null when there is none within reach or Alt holds the snapping off.</summary>
    private Point? SnapToDimensionLines(Point raw, Shape? owner, int ownerSegment, SKPoint direction)
    {
        _dimensionLineGuide = null;
        if (_context is null || WallSnappingSuspended)
        {
            return null;
        }

        var snap = DimensionLineSnapper.Snap(
            _context.Document, owner, ownerSegment, new SKPoint((float)raw.X, (float)raw.Y), direction, (float)(8 / Zoom));
        if (snap is null)
        {
            return null;
        }

        _pointSnap = null;
        _snapGuides = [];
        _dimensionLineGuide = (snap.From, snap.To);
        return new Point(snap.Point.X, snap.Point.Y);
    }

    /// <summary>The number is let go: after a drag the line stays where it is (one undo step); after a click the length editor opens.</summary>
    private void EndDimensionPress()
    {
        var shape = _dimensionShape;
        var symbol = _dimensionOfSymbol;
        var segment = _dimensionSegment;
        var before = _dimensionBefore;
        var dragged = _dimensionDragging;
        ResetDimensionPress();

        if (_context is null || shape is null || before is not { })
        {
            return;
        }

        if (!dragged)
        {
            if (shape.Kind == ShapeKind.Wall)
            {
                BeginWallLengthEdit(shape, segment);
            }
            else if (symbol is not null)
            {
                BeginSymbolDimensionEdit(shape, symbol);
            }

            return;
        }

        var after = ShapeSnapshot.Capture(shape);
        if (!after.Equals(before.Value))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before.Value], [after]));
        }

        _context.RequestRedraw();
    }

    private void ResetDimensionPress()
    {
        _dimensionShape = null;
        _dimensionOfSymbol = null;
        _dimensionBefore = null;
        _dimensionDragging = false;
        ReleaseMouseCapture();
    }

    /// <summary>Esc while a dimension line is being dragged: it jumps back to where it was. True when there was a drag.</summary>
    public bool CancelDimensionPress()
    {
        if (_dimensionShape is not { } shape || _dimensionBefore is not { } before)
        {
            return false;
        }

        if (_dimensionDragging)
        {
            before.ApplyTo(shape);
        }

        ResetDimensionPress();
        _context?.RequestRedraw();
        return true;
    }

    /// <summary>The way the line of a free dimension runs: along its first two points (aligned), the x axis (horizontal) or the y axis (vertical).
    /// Null for the kinds that have no straight line (angle, radius, diameter, length) and while the first two points are not there.</summary>
    private static SKPoint? FreeDimensionDirection(Shape dimension)
    {
        if (dimension.Points is not { Count: >= 2 } points)
        {
            return null;
        }

        return dimension.DimKind switch
        {
            DimensionKind.Horizontal => new SKPoint(1, 0),
            DimensionKind.Vertical => new SKPoint(0, 1),
            DimensionKind.Aligned when Math.Abs(points[1].X - points[0].X) + Math.Abs(points[1].Y - points[0].Y) > 0.01
                => new SKPoint((float)(points[1].X - points[0].X), (float)(points[1].Y - points[0].Y)),
            _ => null,
        };
    }

    /// <summary>The dashed line along the dimension line that the dragged one has lined up with, with a small bar at each end.</summary>
    private void DrawDimensionLineGuide(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_dimensionLineGuide is not { } guide)
        {
            return;
        }

        using var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, Color = MeasureOrange.WithAlpha(70) };
        using var line = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, Color = MeasureOrange,
            PathEffect = SKPathEffect.CreateDash([6, 4], 0),
        };
        var from = new SKPoint(guide.From.X * scaleX, guide.From.Y * scaleY);
        var to = new SKPoint(guide.To.X * scaleX, guide.To.Y * scaleY);
        canvas.DrawLine(from, to, glow);
        canvas.DrawLine(from, to, line);
    }

    /// <summary>Gives the dimension of one wall segment its own distance and side (the same entry the DIMENSION card edits).</summary>
    private static void SetWallDimensionLine(Shape wall, int segment, double offset, bool flip)
    {
        var list = (wall.WallDimensionOverrides ?? []).ToList();
        var current = list.FirstOrDefault(o => o.Segment == segment) ?? new WallDimensionOverride(segment);
        list.RemoveAll(o => o.Segment == segment);
        list.Add(current with { Offset = offset, Flip = flip });
        wall.WallDimensionOverrides = list.OrderBy(o => o.Segment).ToList();
    }
}
