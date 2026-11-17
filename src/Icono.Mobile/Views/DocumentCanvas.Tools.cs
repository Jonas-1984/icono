using Avalonia;
using Icono.Core;
using Icono.Editor;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The drawing tools on the tablet. Shapes and two-point lines are dragged open with one finger (a tap gives a door, window or symbol
/// its real size, a rectangle or ellipse a 2.5 cm square). Walls, polylines and dimensions are drawn point by point: put the finger down, slide
/// it to the right place (it snaps to the points of other objects, walls also to corners, axes and alignments) and lift it - that places the
/// point. The new objects get the same defaults as on Windows (<see cref="ShapeTemplates"/>).</summary>
public sealed partial class DocumentCanvas
{
    /// <summary>The size a rectangle, ellipse ... gets from a plain tap: 2.5 cm on the paper (96 units per inch).</summary>
    private const double TapShapeSize = 2.5 / 2.54 * 96;

    // a shape or two-point line being dragged open
    private Shape? _pendingShape;
    private Point _placeStart;

    // a wall, polyline or dimension being drawn point by point: the last point follows the finger (it is the "ghost" while no finger is down)
    private Shape? _pendingPath;
    private bool _placingFirstPoint;

    /// <summary>Where the point being placed snapped to, and to what (shown while the finger is down).</summary>
    private (double X, double Y, string Label)? _snapMark;

    /// <summary>Raised when a wall / polyline / dimension starts or ends, or gets a point (the window shows Done / Cancel then).</summary>
    public event EventHandler? PathDrawingChanged;

    /// <summary>True while a wall, polyline or dimension is being drawn.</summary>
    public bool IsDrawingPath => _pendingPath is not null;

    /// <summary>How many points the wall / polyline / dimension being drawn has placed.</summary>
    public int PathPointCount => _pendingPath?.Points is { } points ? Math.Max(0, points.Count - 1) : 0;

    private bool ToolIsActive => _context is { ActiveTool: ActiveTool.Shape or ActiveTool.PathDraw or ActiveTool.Freehand };

    // ---------------------------------------------------------------- finger down / moves / up

    private void BeginToolPress(Point view)
    {
        if (_context is not { } context)
        {
            return;
        }

        var at = ToDocument(view);
        if (context.ActiveTool == ActiveTool.Freehand)
        {
            // handwriting: every stroke is one object (DocumentCanvas.Ink.cs)
            BeginInk(at);
            _gesture = Gesture.Ink;
            return;
        }

        if (context.ActiveTool == ActiveTool.Shape && context.SelectedShapeKind == ShapeKind.Text)
        {
            // the text is typed once the finger lifts (DocumentCanvas.EndPointer)
            _gesture = Gesture.TextTap;
            return;
        }

        if (context.ActiveTool == ActiveTool.Shape)
        {
            var kind = context.SelectedShapeKind;
            var isLine = ShapeTemplates.TwoPointLineKinds.Contains(kind);
            var start = SnapToObjectPoints(at, exclude: []) ?? at;
            _placeStart = start;
            _pendingShape = isLine
                ? ShapeTemplates.NewTwoPointLine(kind, start.X, start.Y, context.CurrentFillColor)
                : ShapeTemplates.NewBox(kind, start.X, start.Y, context.CurrentFillColor, context.PendingUnit);
            _gesture = Gesture.Place;
        }
        else
        {
            if (_pendingPath is null)
            {
                var first = SnapPathPoint(at, null);
                _pendingPath = ShapeTemplates.NewPath(
                    context.SelectedShapeKind, first.X, first.Y, context.CurrentFillColor, context.CurrentDimensionKind, context.DimensionUnit,
                    context.Document.ScaleDenominator);
                _placingFirstPoint = true;
                PathDrawingChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SetGhost(SnapPathPoint(at, _pendingPath));
            }

            _gesture = Gesture.PathPoint;
        }

        context.RequestRedraw();
    }

    private void UpdateTool(Point view)
    {
        var at = ToDocument(view);
        if (_gesture == Gesture.Place && _pendingShape is { } shape)
        {
            if (shape.Points is not null)
            {
                var start = new SKPoint((float)_placeStart.X, (float)_placeStart.Y);
                var end = SnapToObjectPoints(at, exclude: [], reference: start) ?? at;

                // re-anchored to the fixed start every time, so the bounds recomputation never drifts
                shape.X = _placeStart.X;
                shape.Y = _placeStart.Y;
                shape.Points = [new CorePoint(0, 0), new CorePoint(end.X - _placeStart.X, end.Y - _placeStart.Y)];
                PathBoundsCalculator.Recompute(shape);
            }
            else
            {
                var corner = SnapToObjectPoints(at, exclude: []) ?? at;
                var outline = ShapeOutline.FrameForBox(
                    shape,
                    Math.Min(_placeStart.X, corner.X),
                    Math.Min(_placeStart.Y, corner.Y),
                    Math.Abs(corner.X - _placeStart.X),
                    Math.Abs(corner.Y - _placeStart.Y));
                (shape.X, shape.Y, shape.Width, shape.Height) = (outline.X, outline.Y, outline.W, outline.H);
            }
        }
        else if (_gesture == Gesture.PathPoint && _pendingPath is { } path)
        {
            if (_placingFirstPoint)
            {
                // the first point (and the ghost on it) follows the finger until it lifts
                var first = SnapPathPoint(at, null);
                path.X = first.X;
                path.Y = first.Y;
            }
            else
            {
                SetGhost(SnapPathPoint(at, path));
            }
        }

        _context?.RequestRedraw();
    }

    private void FinishToolPress()
    {
        _snapMark = null;
        if (_gesture == Gesture.Place)
        {
            CommitPendingShape();
        }
        else if (_gesture == Gesture.PathPoint)
        {
            PlacePathPoint();
        }
    }

    /// <summary>A second finger came down: the shape being dragged is dropped, the path point being placed goes back to where it was.</summary>
    private void CancelToolPress()
    {
        _snapMark = null;
        if (_gesture == Gesture.Place)
        {
            _pendingShape = null;
        }
        else if (_gesture == Gesture.PathPoint && _pendingPath is { Points: { } points } path)
        {
            if (_placingFirstPoint)
            {
                CancelPath();
                return;
            }

            var list = points.ToList();
            list[^1] = list[^2];
            path.Points = list;
            PathBoundsCalculator.Recompute(path);
        }

        _context?.RequestRedraw();
    }

    // ---------------------------------------------------------------- shapes

    private void CommitPendingShape()
    {
        var shape = _pendingShape;
        _pendingShape = null;
        if (shape is null || _context is not { } context)
        {
            return;
        }

        ShapeTemplates.ApplyClickSize(shape, context.Document, context.PendingUnit);
        if (shape.Points is null && !ShapeTemplates.IsBigEnough(shape))
        {
            // a tap: a rectangle, ellipse ... of a handy size
            var outline = ShapeOutline.FrameForBox(shape, shape.X, shape.Y, TapShapeSize, TapShapeSize);
            (shape.X, shape.Y, shape.Width, shape.Height) = (outline.X, outline.Y, outline.W, outline.H);
        }

        if (!ShapeTemplates.IsBigEnough(shape))
        {
            context.RequestRedraw();
            return;
        }

        Insert(shape);
        if (shape.Points is not null)
        {
            ConnectionRouter.AttachEnds(context.Document, shape);
        }

        // as on Windows: after placing a shape the tool goes back to Select, so the new shape can be checked and moved
        context.ActiveTool = ActiveTool.Select;
        context.RequestRedraw();
    }

    private void Insert(Shape shape)
    {
        if (_context is not { } context || (context.SelectedLayer ?? context.Document.Layers.FirstOrDefault()) is not { } layer)
        {
            return;
        }

        layer.Shapes.Insert(0, shape);
        context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
        context.SelectedLayer = layer;
        context.SelectedShape = shape;
    }

    // ---------------------------------------------------------------- walls, polylines, dimensions

    /// <summary>The finger lifted: the point it was placing stays; a wall that came back to its start closes into a room, a dimension with all
    /// its points is done.</summary>
    private void PlacePathPoint()
    {
        if (_pendingPath is not { Points: { } points } path || _context is null)
        {
            return;
        }

        if (_placingFirstPoint)
        {
            _placingFirstPoint = false; // the ghost already sits on the first point
            PathDrawingChanged?.Invoke(this, EventArgs.Empty);
            _context.RequestRedraw();
            return;
        }

        var placed = points.Count - 1;
        if (path.Kind == ShapeKind.Wall && placed >= 3 && IsAtStart(path, points[^1]))
        {
            FinishPath(closeWall: true);
            return;
        }

        var list = points.ToList();
        list.Add(list[^1]);
        path.Points = list;

        if (path.Kind == ShapeKind.Dimension && list.Count - 1 >= ShapeTemplates.DimensionPointsNeeded(path.DimKind))
        {
            FinishPath();
            return;
        }

        PathDrawingChanged?.Invoke(this, EventArgs.Empty);
        _context.RequestRedraw();
    }

    /// <summary>Ends the wall / polyline / dimension being drawn and adds it to the drawing (as Enter or a double click on Windows).</summary>
    public void FinishPath(bool closeWall = false)
    {
        var shape = _pendingPath;
        _pendingPath = null;
        _placingFirstPoint = false;
        if (shape?.Points is not { } current || _context is not { } context)
        {
            PathDrawingChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // the trailing ghost only ever followed the finger
        var points = current.ToList();
        points.RemoveAt(points.Count - 1);
        if (points.Count < 2 || (shape.Kind == ShapeKind.Dimension && points.Count < ShapeTemplates.DimensionPointsNeeded(shape.DimKind)))
        {
            PathDrawingChanged?.Invoke(this, EventArgs.Empty);
            context.RequestRedraw();
            return;
        }

        shape.Points = points;
        if (closeWall && shape.Kind == ShapeKind.Wall && points.Count >= 3)
        {
            shape.WallClosed = true;
        }

        PathBoundsCalculator.Recompute(shape);

        // a wall that ends free gives back what was added for a corner that never came (typed lengths, see DocumentCanvas.TypedWall.cs)
        if (shape.Kind == ShapeKind.Wall)
        {
            if (shape.WallClosed)
            {
                WallTyping?.Forget();
            }
            else
            {
                WallTyping?.FitLast(shape);
            }
        }

        Insert(shape);
        ConnectionRouter.AttachEnds(context.Document, shape);
        if (shape.Kind == ShapeKind.Dimension)
        {
            // a dimension whose points sit on a shape sticks to it: it follows when the shape moves or is resized
            DimensionRouter.AttachPoints(context.Document, shape);
        }

        PathDrawingChanged?.Invoke(this, EventArgs.Empty);
        context.RequestRedraw();
    }

    /// <summary>Drops the wall / polyline / dimension being drawn.</summary>
    public void CancelPath()
    {
        _pendingPath = null;
        _placingFirstPoint = false;
        _snapMark = null;
        WallTyping?.Clear();
        WallTyping?.Forget();
        PathDrawingChanged?.Invoke(this, EventArgs.Empty);
        _context?.RequestRedraw();
    }

    /// <summary>Takes back the last placed point of the wall / polyline / dimension being drawn (the first one ends it).</summary>
    public void RemoveLastPathPoint()
    {
        if (_pendingPath is not { Points: { } points } path)
        {
            return;
        }

        if (points.Count <= 2)
        {
            CancelPath();
            return;
        }

        var list = points.ToList();
        list.RemoveAt(list.Count - 1);
        list[^1] = list[^2]; // the ghost waits on the point before
        path.Points = list;
        PathBoundsCalculator.Recompute(path);
        WallTyping?.Forget(); // the typed numbers belong to corners that are not all there any more
        PathDrawingChanged?.Invoke(this, EventArgs.Empty);
        _context?.RequestRedraw();
    }

    private void SetGhost(Point at)
    {
        if (_pendingPath is not { Points: { } points } path)
        {
            return;
        }

        var list = points.ToList();
        list[^1] = new CorePoint(at.X - path.X, at.Y - path.Y);
        path.Points = list;
        PathBoundsCalculator.Recompute(path);
    }

    private bool IsAtStart(Shape wall, CorePoint point)
    {
        var start = wall.Points![0];
        var (dx, dy) = (point.X - start.X, point.Y - start.Y);
        return Math.Sqrt(dx * dx + dy * dy) <= FingerReach * 1.6 / _zoom;
    }

    // ---------------------------------------------------------------- snapping (the shared snappers of the Windows program)

    /// <summary>A point of another object near <paramref name="at"/> (end, middle, centre, crossing ...), or null.</summary>
    private Point? SnapToObjectPoints(Point at, IReadOnlyCollection<Shape> exclude, SKPoint? reference = null)
    {
        _snapMark = null;
        if (_context is not { } context)
        {
            return null;
        }

        var result = PointSnapper.Snap(context.Document, new SKPoint((float)at.X, (float)at.Y), FingerReach / _zoom, context.PointSnap, exclude, reference);
        if (result is null)
        {
            return null;
        }

        _snapMark = (result.Point.X, result.Point.Y, result.Label);
        return new Point(result.Point.X, result.Point.Y);
    }

    /// <summary>Where the point of a wall / polyline / dimension goes: back on the start of a wall (closes the room), on a point of another
    /// object, or - for a wall - on the corners, axes and alignments of the wall tool.</summary>
    private Point SnapPathPoint(Point at, Shape? path)
    {
        _snapMark = null;
        if (_context is not { } context)
        {
            return at;
        }

        var kind = path?.Kind ?? context.SelectedShapeKind;
        List<SKPoint> placed = path is null ? [] : WallGeometry.AbsolutePoints(path);
        if (placed.Count > 0)
        {
            placed.RemoveAt(placed.Count - 1); // the ghost itself
        }

        SKPoint? previous = placed.Count > 0 ? placed[^1] : null;

        if (kind == ShapeKind.Wall && path?.Points is { } own && placed.Count >= 3)
        {
            var start = new Point(path.X + own[0].X, path.Y + own[0].Y);
            if (Math.Sqrt(Math.Pow(at.X - start.X, 2) + Math.Pow(at.Y - start.Y, 2)) <= FingerReach * 1.6 / _zoom)
            {
                _snapMark = (start.X, start.Y, "Close room");
                return start;
            }
        }

        if (SnapToObjectPoints(at, exclude: [], reference: previous) is { } onPoint)
        {
            return onPoint;
        }

        if (kind == ShapeKind.Wall)
        {
            var thickness = path?.WallThickness ?? new Shape { Kind = ShapeKind.Wall }.WallThickness;
            var result = WallSnapper.Snap(
                new SKPoint((float)at.X, (float)at.Y), previous, placed, context.Document, context.WallSnap, FingerReach / _zoom, wallThickness: thickness);
            if (result.Snapped)
            {
                _snapMark = (result.Point.X, result.Point.Y, "");
                return new Point(result.Point.X, result.Point.Y);
            }
        }

        return at;
    }

    /// <summary>The marks of the drawing under way, in drawing units: the placed points of a path and where the current point snapped.</summary>
    private List<(double X, double Y)> PathPointMarks()
    {
        if (_pendingPath is not { Points: { } points } path)
        {
            return [];
        }

        var count = _placingFirstPoint ? 1 : points.Count - 1;
        return points.Take(Math.Max(1, count)).Select(p => (path.X + p.X, path.Y + p.Y)).ToList();
    }

    /// <summary>What to hand the renderer as the object under way (a path only once it has some length).</summary>
    private Shape? PendingForRender() =>
        _inkShape ?? _pendingShape ?? (_pendingPath is { Points.Count: >= 2 } path && !_placingFirstPoint ? path : null);
}
