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

    // polyline: the next side is an arc through three points (the start, a point on the arc, the end), as the Arc option of the AutoCAD polyline
    private bool _arcMode;
    private CorePoint? _arcMid; // the point on the arc, in drawing units, once it is tapped

    /// <summary>Where the point being placed snapped to, and to what (shown while the finger is down).</summary>
    private (double X, double Y, string Label)? _snapMark;

    /// <summary>The kind of point and the guide lines of the snap that <see cref="_snapMark"/> shows (null for the other helpers: the room closing, the wall tool).</summary>
    private PointSnapResult? _snapDetail;

    /// <summary>The helpers of the wall tool that were applied (straight, parallel, in line with, same length): their guide lines and the length - angle label.</summary>
    private WallSnapResult? _wallSnapResult;

    /// <summary>Raised when a wall / polyline / dimension starts or ends, or gets a point (the window shows Done / Cancel then).</summary>
    public event EventHandler? PathDrawingChanged;

    /// <summary>True while a wall, polyline or dimension is being drawn.</summary>
    public bool IsDrawingPath => _pendingPath is not null;

    /// <summary>True while the sides of the polyline being drawn are arcs (the Arc key of the drawing bar is on).</summary>
    public bool ArcMode => _arcMode;

    /// <summary>True when the point on the arc is placed and the end point is what the next tap gives.</summary>
    public bool ArcWaitingForEnd => _arcMid is not null;

    /// <summary>True while a polyline is being drawn and has its first point: the sides can be switched to arcs (and back).</summary>
    public bool CanUseArcs => _pendingPath is { Kind: ShapeKind.Polyline } && !_placingFirstPoint;

    /// <summary>The Arc key: the next sides of the polyline are arcs - a tap gives a point on the arc, the next tap its end - until the key is pressed again, then
    /// the sides are straight again.</summary>
    public void ToggleArcMode()
    {
        if (_pendingPath is not { Kind: ShapeKind.Polyline } path || _placingFirstPoint)
        {
            return;
        }

        _arcMode = !_arcMode;
        _arcMid = null;
        UpdateArcPreview(path);
        PathBoundsCalculator.Recompute(path);
        PathDrawingChanged?.Invoke(this, EventArgs.Empty);
        _context?.RequestRedraw();
    }

    /// <summary>The side from the last placed point to the point that follows the finger: straight, or - once the point on the arc is placed - the arc through
    /// the three points. The entry of that side in the bulges of the polyline being drawn is what the renderer draws.</summary>
    private void UpdateArcPreview(Shape path)
    {
        if (path.Points is not { Count: >= 2 } points || (path.SegmentBulges is null && _arcMid is null))
        {
            return;
        }

        var bulges = path.SegmentBulges is { } given && given.Count == points.Count ? given.ToList() : Enumerable.Repeat(0.0, points.Count).ToList();
        var start = new CorePoint(path.X + points[^2].X, path.Y + points[^2].Y);
        var ghost = new CorePoint(path.X + points[^1].X, path.Y + points[^1].Y);
        bulges[^2] = _arcMid is { } mid ? ArcSegment.FromThreePoints(start, mid, ghost) : 0;
        path.SegmentBulges = bulges.Any(ArcSegment.IsArc) ? bulges : null;
    }

    /// <summary>Forgets a point on the arc that was placed (the arc is not finished): the side goes back to a straight one.</summary>
    private void DropArcMid(Shape path)
    {
        if (_arcMid is null)
        {
            return;
        }

        _arcMid = null;
        UpdateArcPreview(path);
        PathBoundsCalculator.Recompute(path);
    }

    /// <summary>How many points the wall / polyline / dimension being drawn has placed.</summary>
    public int PathPointCount => _pendingPath?.Points is { } points ? Math.Max(0, points.Count - 1) : 0;

    private bool ToolIsActive => _context is { ActiveTool: ActiveTool.Shape or ActiveTool.PathDraw or ActiveTool.Freehand or ActiveTool.CornerCurve or ActiveTool.Measure or ActiveTool.RegionColor } || (_context is not null && IsTapTool(_context.ActiveTool));

    // ---------------------------------------------------------------- finger down / moves / up

    private void BeginToolPress(Point view)
    {
        if (_context is not { } context)
        {
            return;
        }

        var at = ToDocument(view);
        if (IsTapTool(context.ActiveTool))
        {
            // Match, colour picker, Add / Delete Point, Cut Wall, distance: a tap does it (DocumentCanvas.MoreTools.cs)
            _gesture = Gesture.ToolTap;
            return;
        }

        if (context.ActiveTool == ActiveTool.Measure)
        {
            BeginRuler(at);
            _gesture = Gesture.Ruler;
            return;
        }

        if (context.ActiveTool == ActiveTool.RegionColor)
        {
            BeginBucket(context, at);
            _gesture = Gesture.Bucket;
            return;
        }

        if (context.ActiveTool == ActiveTool.CornerCurve)
        {
            // the sides of a corner are tapped (DocumentCanvas.Curve.cs), when the finger lifts
            _gesture = Gesture.CurveTap;
            return;
        }

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
            var start = SnapToObjectPoints(at, exclude: []) ?? GridPoint(at);
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
                var end = SnapLinePoint(at, [start], start, null) ?? GridPoint(at);

                // re-anchored to the fixed start every time, so the bounds recomputation never drifts
                shape.X = _placeStart.X;
                shape.Y = _placeStart.Y;
                shape.Points = [new CorePoint(0, 0), new CorePoint(end.X - _placeStart.X, end.Y - _placeStart.Y)];
                PathBoundsCalculator.Recompute(shape);
            }
            else
            {
                var corner = SnapToObjectPoints(at, exclude: []) ?? GridPoint(at);
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
        _snapDetail = null;
        _wallSnapResult = null;
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
        _snapDetail = null;
        _wallSnapResult = null;
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

        if (_arcMode && path.Kind == ShapeKind.Polyline && _arcMid is null)
        {
            // the point on the arc is placed; the end point comes next
            _arcMid = new CorePoint(path.X + points[^1].X, path.Y + points[^1].Y);
            UpdateArcPreview(path);
            PathBoundsCalculator.Recompute(path);
            PathDrawingChanged?.Invoke(this, EventArgs.Empty);
            _context.RequestRedraw();
            return;
        }

        if (path.Kind == ShapeKind.Polyline)
        {
            UpdateArcPreview(path); // the arc through its three points is final now
            _arcMid = null;
        }

        var placed = points.Count - 1;
        if (path.Kind is ShapeKind.Wall or ShapeKind.Polyline && placed >= 3 && IsAtStart(path, points[^1]))
        {
            FinishPath(closeWall: true);
            return;
        }

        var list = points.ToList();
        list.Add(list[^1]);
        if (path.SegmentBulges is { } bulges && bulges.Count == points.Count)
        {
            path.SegmentBulges = bulges.Append(0).ToList(); // the point that follows the finger starts a straight side
        }

        path.Points = list;
        PathBoundsCalculator.Recompute(path);

        if (path.Kind == ShapeKind.Dimension && list.Count - 1 >= ShapeTemplates.DimensionPointsNeeded(path.DimKind))
        {
            FinishPath();
            return;
        }

        PathDrawingChanged?.Invoke(this, EventArgs.Empty);
        _context.RequestRedraw();
    }

    /// <summary>True while a wall or polyline has three points: its two ends can be joined (the Close key of the drawing bar).</summary>
    public bool CanClosePath => _pendingPath is { Kind: ShapeKind.Wall or ShapeKind.Polyline, Points.Count: >= 4 } && !_placingFirstPoint;

    /// <summary>Joins the two ends of the wall or polyline being drawn and ends it: a room, or a closed shape that takes a fill.</summary>
    public void ClosePath() => FinishPath(closeWall: true);

    /// <summary>Ends the wall / polyline / dimension being drawn and adds it to the drawing (as Enter or a double click on Windows).</summary>
    public void FinishPath(bool closeWall = false)
    {
        var shape = _pendingPath;
        if (shape is not null)
        {
            DropArcMid(shape); // an arc that was not finished goes
        }

        _pendingPath = null;
        _arcMode = false;
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

        if (shape.SegmentBulges is { } arcs)
        {
            // the ghost goes with its side; the side after the last point means nothing for an open line, but it is the closing side of a closed shape
            var kept = arcs.Take(points.Count).ToList();
            if (kept.Count == points.Count && !(closeWall && shape.Kind == ShapeKind.Polyline))
            {
                kept[^1] = 0;
            }

            shape.SegmentBulges = kept.Count == points.Count && kept.Any(ArcSegment.IsArc) ? kept : null;
        }

        shape.Points = points;
        if (closeWall && shape.Kind == ShapeKind.Wall && points.Count >= 3)
        {
            shape.WallClosed = true;
        }

        PathBoundsCalculator.Recompute(shape);
        if (closeWall && shape.Kind == ShapeKind.Polyline && points.Count >= 3)
        {
            PolylineClosing.Close(shape); // the two ends are joined: a closed shape that takes a fill
        }

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
        _arcMode = false;
        _arcMid = null;
        _placingFirstPoint = false;
        _snapMark = null;
        _snapDetail = null;
        _wallSnapResult = null;
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

        if (_arcMid is not null)
        {
            // the point on the arc is taken back first
            DropArcMid(path);
            PathDrawingChanged?.Invoke(this, EventArgs.Empty);
            _context?.RequestRedraw();
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
        if (path.SegmentBulges is { } bulges && bulges.Count == points.Count)
        {
            // the side to the point that went is gone, and the new last side (to the ghost) is straight
            var kept = bulges.Take(list.Count).ToList();
            kept[^1] = 0;
            kept[^2] = 0;
            path.SegmentBulges = kept.Any(ArcSegment.IsArc) ? kept : null;
        }

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
        if (path.Kind == ShapeKind.Polyline)
        {
            UpdateArcPreview(path);
        }

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
        _snapDetail = null;
        _wallSnapResult = null;
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
        _snapDetail = result;
        return new Point(result.Point.X, result.Point.Y);
    }

    /// <summary>Where the point of a wall / polyline / dimension goes: back on the start of a wall (closes the room), on a point of another
    /// object, or - for a wall - on the corners, axes and alignments of the wall tool.</summary>
    private Point SnapPathPoint(Point at, Shape? path)
    {
        _snapMark = null;
        _snapDetail = null;
        _wallSnapResult = null;
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

        if (kind is ShapeKind.Wall or ShapeKind.Polyline && path?.Points is { } own && placed.Count >= 3)
        {
            var start = new Point(path.X + own[0].X, path.Y + own[0].Y);
            if (Math.Sqrt(Math.Pow(at.X - start.X, 2) + Math.Pow(at.Y - start.Y, 2)) <= FingerReach * 1.6 / _zoom)
            {
                _snapMark = (start.X, start.Y, kind == ShapeKind.Wall ? "Close room" : "Close shape");
                return start;
            }
        }

        if (kind != ShapeKind.Wall && IsLineLike(kind))
        {
            // the points of the objects AND of the part of this line that is there already, then the helpers of the wall tool: as in the Windows program
            return SnapLinePoint(at, placed, previous, null) ?? GridPoint(at);
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
                _snapMark = (result.Point.X, result.Point.Y, result.Label);
                _wallSnapResult = result;
                return new Point(result.Point.X, result.Point.Y);
            }
        }

        return GridPoint(at);
    }

    /// <summary>The marks of the drawing under way, in drawing units: the placed points of a path and where the current point snapped.</summary>
    private List<(double X, double Y)> PathPointMarks()
    {
        if (_pendingPath is not { Points: { } points } path)
        {
            return [];
        }

        var count = _placingFirstPoint ? 1 : points.Count - 1;
        var marks = points.Take(Math.Max(1, count)).Select(p => (path.X + p.X, path.Y + p.Y)).ToList();
        if (_arcMid is { } mid)
        {
            marks.Add((mid.X, mid.Y)); // the point on the arc
        }

        return marks;
    }

    /// <summary>What to hand the renderer as the object under way (a path only once it has some length).</summary>
    private Shape? PendingForRender() =>
        _inkShape ?? _pendingShape ?? (_pendingPath is { Points.Count: >= 2 } path && !_placingFirstPoint ? path : null);
}
