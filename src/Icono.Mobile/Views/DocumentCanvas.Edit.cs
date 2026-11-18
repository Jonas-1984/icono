using Avalonia;
using Icono.Core;
using Icono.Editor;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>Selecting, moving, resizing, turning and point editing (the geometry is shared with the Windows program:
/// <see cref="ShapeResizer"/>, <see cref="WallStraightener"/>, <see cref="ConnectionRouter"/> ...).</summary>
public sealed partial class DocumentCanvas
{
    /// <summary>Edge length of a square resize handle on screen (DIP) - bigger than on Windows, for a finger.</summary>
    private const float HandleSize = 10;

    /// <summary>Radius of the round handles (turn, points) on screen (DIP).</summary>
    private const float RoundHandleRadius = 7;

    /// <summary>How far above the top edge the turn handle sits (DIP).</summary>
    private const double RotateHandleOffset = 28;

    /// <summary>How far from a handle a finger still takes it (DIP).</summary>
    private const double HandleReach = 20;

    /// <summary>A turn closer than this to a multiple of 15 degrees snaps to it.</summary>
    private const double RotationSnap = 4;

    private enum HandleKind { Resize, Rotate, Point }

    /// <summary>One handle of the selection, in drawing units. <see cref="Index"/> is the point index of a point handle.</summary>
    private readonly record struct HandleMark(HandleKind Kind, double X, double Y, ResizeHandle Resize = default, int Index = -1);

    // the shapes an edit changes and how they were before it (for undo, or to take it back)
    private List<Shape> _editShapes = [];
    private List<ShapeSnapshot> _editBefore = [];

    // move
    private List<(double X, double Y)> _moveStart = [];
    private Point _moveStartDocument;

    // resize of one shape / of a multi-selection
    private ResizeHandle _resizeHandle;
    private (double X, double Y, double W, double H) _resizeStartOutline;
    private (double X, double Y, double W, double H) _resizeStartFrame;
    private (double X, double Y) _resizeStartPoint;
    private double _resizeRotation;
    private (double X, double Y) _resizePivot;
    private List<(double X, double Y, double W, double H)> _groupStartRects = [];

    // point drag
    private int _pointIndex;

    /// <summary>The angle shown next to the turn handle while it is dragged.</summary>
    private string? _rotationLabel;

    private static bool IsLocked(Shape shape, Layer layer) => shape.Locked || layer.Locked;

    private bool SelectionIsEditable =>
        _context is { SelectedShapes.Count: > 0 } context && context.SelectedShapes.All(s => !s.Locked && LayerOf(s) is not { Locked: true });

    private Layer? LayerOf(Shape shape) => _context?.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));

    // ----- tap: select

    private void Tap()
    {
        if (_context is null)
        {
            return;
        }

        if (AreaMode)
        {
            // in the area mode a tap adds an object to the selection or takes it out again
            if (_pressHit is { } picked)
            {
                ToggleSelected(picked.Shape, picked.Layer);
            }
            else
            {
                _context.SelectedShapes.Clear();
            }
        }
        else if (_pressNumber is { } number)
        {
            // a tap on the number of a dimension: type the length (DocumentCanvas.Length.cs)
            TapNumber(number);
            return;
        }
        else if (_pressHit is { } hit)
        {
            Select(hit.Shape, hit.Layer);
        }
        else
        {
            _context.SelectedShapes.Clear();
        }

        _context.RequestRedraw();
    }

    /// <summary>Selects a shape; any shape of a group selects the whole group (as on Windows).</summary>
    private void Select(Shape shape, Layer layer)
    {
        if (_context is null || _context.SelectedShapes.Contains(shape))
        {
            return;
        }

        _context.SelectedShapes.Clear();
        if (shape.GroupId is { } groupId)
        {
            foreach (var mate in _context.Document.Layers.SelectMany(l => l.Shapes).Where(s => s.GroupId == groupId))
            {
                _context.SelectedShapes.Add(mate);
            }
        }
        else
        {
            _context.SelectedShapes.Add(shape);
        }

        _context.SelectedLayer = layer;
    }

    // ----- the handles of the selection

    /// <summary>The handles the current selection offers: eight resize handles and a turn handle for one shape, point handles for a
    /// line / wall / dimension, eight resize handles round a multi-selection; none for locked shapes.</summary>
    private List<HandleMark> SelectionHandles()
    {
        var handles = new List<HandleMark>();
        if (_context is null || !SelectionIsEditable)
        {
            return handles;
        }

        var selected = _context.SelectedShapes;
        if (selected.Count > 1)
        {
            handles.AddRange(ShapeResizer.BoxHandles(ShapeResizer.GroupBounds(selected)).Select(h => new HandleMark(HandleKind.Resize, h.X, h.Y, h.Handle)));
            return handles;
        }

        var shape = selected[0];
        if (shape.Points is { Count: > 0 } points)
        {
            handles.AddRange(points.Select((p, i) => new HandleMark(HandleKind.Point, shape.X + p.X, shape.Y + p.Y, Index: i)));
            return handles;
        }

        handles.AddRange(ShapeResizer.Handles(shape).Select(h => new HandleMark(HandleKind.Resize, h.X, h.Y, h.Handle)));
        var (rx, ry) = ShapeResizer.RotateHandle(shape, RotateHandleOffset / _zoom);
        handles.Add(new HandleMark(HandleKind.Rotate, rx, ry));
        return handles;
    }

    /// <summary>How close a finger on the object itself must come to a handle to take it instead of moving the object (DIP).</summary>
    private const double HandleExactReach = 9;

    /// <summary>True when the finger is on one of the selected objects.</summary>
    private bool IsOnSelection(Point view)
    {
        if (_context is not { } context)
        {
            return false;
        }

        var at = ToDocument(view);
        return ShapeHitTester.Find(context.Document, at.X, at.Y, _zoom, FingerReach) is { } hit && context.SelectedShapes.Contains(hit.Shape);
    }

    /// <summary>Starts dragging the handle under the finger, if there is one.</summary>
    private bool TryBeginHandleDrag(Point view)
    {
        if (_context is null)
        {
            return false;
        }

        // A finger ON a selected object means "move it": then a resize or point handle only counts when it is touched almost exactly, so
        // small objects (whose handles crowd their inside) can still be dragged. Beside the object the handles have their full reach.
        var onSelection = IsOnSelection(view);
        var reach = onSelection ? HandleExactReach : HandleReach;

        // the nearest handle within reach; the turn handle wins a tie, it sits on its own
        HandleMark? best = null;
        var bestDistance = reach;
        foreach (var handle in SelectionHandles())
        {
            var at = ToView(handle.X, handle.Y);
            var distance = Math.Sqrt(Math.Pow(at.X - view.X, 2) + Math.Pow(at.Y - view.Y, 2));
            if (distance < bestDistance || (handle.Kind == HandleKind.Rotate && distance <= HandleReach))
            {
                best = handle;
                bestDistance = handle.Kind == HandleKind.Rotate ? -1 : distance;
            }
        }

        if (best is not { } chosen)
        {
            return false;
        }

        var selected = _context.SelectedShapes;
        _editShapes = selected.ToList();
        _editBefore = _editShapes.Select(ShapeSnapshot.Capture).ToList();
        var start = ToDocument(view);
        _resizeStartPoint = (start.X, start.Y);

        switch (chosen.Kind)
        {
            case HandleKind.Rotate:
                _gesture = Gesture.Rotate;
                break;
            case HandleKind.Point:
                _gesture = Gesture.DragPoint;
                _pointIndex = chosen.Index;
                break;
            case HandleKind.Resize when selected.Count > 1:
                _gesture = Gesture.GroupResize;
                _resizeHandle = chosen.Resize;
                _resizeStartOutline = ShapeResizer.GroupBounds(selected);
                _groupStartRects = _editShapes.Select(s => (s.X, s.Y, s.Width, s.Height)).ToList();
                break;
            default:
                var shape = _editShapes[0];
                _gesture = Gesture.Resize;
                _resizeHandle = chosen.Resize;
                _resizeStartOutline = ShapeResizer.Outline(shape);
                _resizeStartFrame = (shape.X, shape.Y, shape.Width, shape.Height);
                _resizeRotation = shape.Rotation;
                _resizePivot = ShapeResizer.Pivot(shape);
                break;
        }

        return true;
    }

    // ----- drag: move the selection

    private bool BeginMove(Shape shape, Layer layer)
    {
        if (_context is null)
        {
            return false;
        }

        Select(shape, layer);
        if (!SelectionIsEditable)
        {
            return false;
        }

        _editShapes = _context.SelectedShapes.ToList();
        _editBefore = _editShapes.Select(ShapeSnapshot.Capture).ToList();
        _moveStartDocument = ToDocument(_pressPoint);
        _moveStart = _editShapes.Select(s => (s.X, s.Y)).ToList();
        return true;
    }

    /// <summary>Follows the finger with the edit under way (always from the frozen start, so nothing drifts).</summary>
    private void UpdateEdit(Point view)
    {
        if (_context is null || _editShapes.Count == 0)
        {
            return;
        }

        var at = ToDocument(view);
        switch (_gesture)
        {
            case Gesture.MoveShapes:
                for (var i = 0; i < _editShapes.Count; i++)
                {
                    _editShapes[i].X = _moveStart[i].X + at.X - _moveStartDocument.X;
                    _editShapes[i].Y = _moveStart[i].Y + at.Y - _moveStartDocument.Y;
                }

                SnapMovedSelection();
                break;

            case Gesture.Resize:
                at = SnapDraggedPoint(at, _editShapes);
                // a corner keeps the proportions (there is no Shift key to lift that on a tablet), an edge stretches one side
                var shape = _editShapes[0];
                (shape.X, shape.Y, shape.Width, shape.Height) = ShapeResizer.ResizeFrame(
                    _resizeHandle, _resizeStartOutline, _resizeStartFrame, _resizeStartPoint, (at.X, at.Y),
                    _resizeRotation, _resizePivot, keepProportions: true, snap: GridRounding);
                break;

            case Gesture.GroupResize:
                at = SnapDraggedPoint(at, _editShapes);
                ShapeResizer.ResizeGroup(
                    _editShapes, _groupStartRects, _resizeHandle, _resizeStartOutline, _resizeStartPoint, (at.X, at.Y), keepProportions: true, snap: GridRounding);
                break;

            case Gesture.Rotate:
                var angle = ShapeResizer.RotationTowards(_editShapes[0], at.X, at.Y);
                angle = ((angle % 360) + 360) % 360;
                var nearest = Math.Round(angle / 15) * 15;
                if (Math.Abs(angle - nearest) <= RotationSnap)
                {
                    angle = nearest % 360;
                }

                _editShapes[0].Rotation = angle;
                _rotationLabel = $"{angle:0.#}°";
                break;

            case Gesture.DragPoint:
                DragPoint(_editShapes[0], at);
                break;
        }

        _context.RequestRedraw();
    }

    private void DragPoint(Shape shape, Point at)
    {
        if (shape.Points is not { } points || _pointIndex < 0 || _pointIndex >= points.Count || _context is null)
        {
            return;
        }

        // a corner of a wall snaps onto the line between its neighbours (two walls become straight) and lines up with other corners;
        // failing that the point goes onto the grid (when the grid snaps)
        var snapped = false;
        if (shape.Kind == ShapeKind.Wall && _context.WallSnap.Enabled)
        {
            var snap = WallStraightener.Snap(_context.Document, shape, _pointIndex, new SKPoint((float)at.X, (float)at.Y), FingerReach / _zoom);
            if (snap.Snapped)
            {
                at = new Point(snap.Point.X, snap.Point.Y);
                snapped = true;
            }
        }

        if (!snapped)
        {
            // on a point of another object (the neighbour in the line lets right angles and parallels work), failing that on the grid
            var neighbour = _pointIndex > 0
                ? new SKPoint((float)(shape.X + points[_pointIndex - 1].X), (float)(shape.Y + points[_pointIndex - 1].Y))
                : points.Count > 1 ? new SKPoint((float)(shape.X + points[1].X), (float)(shape.Y + points[1].Y)) : (SKPoint?)null;
            var onPoint = _context.PointSnap.Enabled ? SnapToObjectPoints(at, [shape], neighbour) : null;
            at = onPoint ?? GridPoint(at);
        }

        var list = points.ToList();
        list[_pointIndex] = new CorePoint(at.X - shape.X, at.Y - shape.Y);
        shape.Points = list;
        PathBoundsCalculator.Recompute(shape);
    }

    /// <summary>The finger lifted: the edit becomes one undo step (when it changed anything).</summary>
    private void FinishEdit()
    {
        _rotationLabel = null;
        ForgetSnapMark();
        if (_context is null || _editShapes.Count == 0)
        {
            return;
        }

        if (_gesture == Gesture.DragPoint)
        {
            var shape = _editShapes[0];
            if (ShapeGeometryFactory.IsLineFamily(shape.Kind))
            {
                // an end dropped exactly on a connection point is glued to it; any other end is free
                ConnectionRouter.AttachEnds(_context.Document, shape);
            }

            if (shape.Kind == ShapeKind.Dimension)
            {
                DimensionRouter.AttachPoints(_context.Document, shape);
            }

            if (shape.Kind == ShapeKind.Wall && WallEditor.CloseWhenEndsMeet(shape))
            {
                _context.Notify("The two ends meet: the wall is a closed room now.");
            }
        }

        var after = _editShapes.Select(ShapeSnapshot.Capture).ToList();
        if (!after.SequenceEqual(_editBefore))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit(_editShapes, _editBefore, after));
        }

        ClearEdit();
        _context.RequestRedraw();
    }

    /// <summary>Takes back an edit under way (a second finger came down: that is a zoom, not an edit).</summary>
    private void CancelEdit()
    {
        _rotationLabel = null;
        ForgetSnapMark();
        if (_context is null || _editShapes.Count == 0 || _gesture is Gesture.Pending or Gesture.View or Gesture.Menu)
        {
            ClearEdit();
            return;
        }

        for (var i = 0; i < _editShapes.Count; i++)
        {
            _editBefore[i].ApplyTo(_editShapes[i]);
        }

        ClearEdit();
        _context.RequestRedraw();
    }

    private void ClearEdit()
    {
        _editShapes = [];
        _editBefore = [];
        _moveStart = [];
        _groupStartRects = [];
        _objectGuides = [];
    }
}
