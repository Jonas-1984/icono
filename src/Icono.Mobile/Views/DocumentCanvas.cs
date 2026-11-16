using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>Text to type: <see cref="Shape"/> is null for a new text object at (<see cref="X"/>, <see cref="Y"/>), else the text object or the
/// shape whose inner text is edited.</summary>
public sealed record TextRequest(Shape? Shape, Layer? Layer, double X, double Y);

/// <summary>Shows and edits the document of an <see cref="EditorContext"/> with the same renderer as the Windows program.
/// <list type="bullet">
/// <item>tap an object: select it (a group as a whole); tap empty paper: select nothing</item>
/// <item>drag an object: move it (with the selection it belongs to) - one undo step</item>
/// <item>drag a handle of the selection: resize it, turn it (round handle) or move a point of a line or wall</item>
/// <item>hold a finger still for half a second: the menu (cut, copy, paste, group, lock, order ...)</item>
/// <item>drag empty paper: move the view; two fingers: zoom and move the view; double tap on empty paper: fit the page</item>
/// </list>
/// A mouse (testing on a PC) works like one finger; the wheel zooms. Editing is in DocumentCanvas.Edit.cs, the menu in
/// DocumentCanvas.Menu.cs, the drawing in DocumentCanvas.Draw.cs.</summary>
public sealed partial class DocumentCanvas : Control
{
    public const double MinZoom = 0.05;
    public const double MaxZoom = 40;

    /// <summary>Space round the page when it is fitted into the view (device-independent pixels).</summary>
    private const double FitMargin = 24;

    /// <summary>How far a finger may wander before a touch counts as a drag instead of a tap (DIP).</summary>
    private const double DragThreshold = 6;

    /// <summary>How far beside thin objects (dimension lines, walls, doors) a finger still hits them (DIP).</summary>
    private const double FingerReach = 12;

    /// <summary>How long a finger has to stay still for the menu.</summary>
    private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(650);

    private EditorContext? _context;
    private double _zoom = 1;
    private Vector _offset;
    private bool _fitPending = true;

    private enum Gesture { None, Pending, Menu, MoveShapes, Resize, GroupResize, Rotate, DragPoint, Place, PathPoint, TextTap, View }

    private Gesture _gesture;

    // the fingers (or the mouse) on the canvas
    private readonly Dictionary<int, Point> _pointers = new();

    // view gesture: the view when it started
    private double _startZoom;
    private Vector _startOffset;
    private Point _startCentre;
    private double _startSpread;

    // the first finger of a gesture
    private Point _pressPoint;
    private (Shape Shape, Layer Layer)? _pressHit;
    private readonly DispatcherTimer _longPressTimer;

    public DocumentCanvas()
    {
        Focusable = true;
        DoubleTapped += OnDoubleTapped;
        _longPressTimer = new DispatcherTimer { Interval = LongPress };
        _longPressTimer.Tick += OnLongPress;
    }

    /// <summary>Raised when <see cref="Zoom"/> changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Screen pixels (DIP) per drawing unit.</summary>
    public double Zoom => _zoom;

    public EditorContext? Context
    {
        get => _context;
        set
        {
            if (_context is not null)
            {
                _context.Invalidated -= OnContextInvalidated;
            }

            _context = value;
            if (_context is not null)
            {
                _context.Invalidated += OnContextInvalidated;
            }

            FitToView();
        }
    }

    private Document? Document => _context?.Document;

    /// <summary>Zooms so that the whole first page is visible and centres it.</summary>
    public void FitToView()
    {
        var document = Document;
        if (document is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _fitPending = true; // done on the first layout pass instead
            return;
        }

        _fitPending = false;
        var availableWidth = Math.Max(1, Bounds.Width - 2 * FitMargin);
        var availableHeight = Math.Max(1, Bounds.Height - 2 * FitMargin);
        var zoom = Math.Min(availableWidth / document.Width, availableHeight / document.Height);
        SetView(zoom, new Vector(
            (Bounds.Width - document.Width * zoom) / 2,
            Math.Max(FitMargin, (Bounds.Height - document.Height * zoom) / 2)));
    }

    /// <summary>The drawing point under a point of this control.</summary>
    public Point ToDocument(Point view) => new((view.X - _offset.X) / _zoom, (view.Y - _offset.Y) / _zoom);

    /// <summary>The point of this control over a drawing point.</summary>
    private Point ToView(double x, double y) => new(x * _zoom + _offset.X, y * _zoom + _offset.Y);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitPending)
        {
            FitToView();
        }
    }

    private void OnContextInvalidated(object? sender, EventArgs e) => InvalidateVisual();

    private void SetView(double zoom, Vector offset)
    {
        var changed = Math.Abs(zoom - _zoom) > 1e-9;
        _zoom = zoom;
        _offset = offset;
        InvalidateVisual();
        if (changed)
        {
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---------------------------------------------------------------- gestures

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var position = e.GetPosition(this);
        _pointers[e.Pointer.Id] = position;
        e.Pointer.Capture(this);
        e.Handled = true;

        if (_pointers.Count == 1)
        {
            _pressPoint = position;

            // a drawing tool draws (DocumentCanvas.Tools.cs)
            if (ToolIsActive)
            {
                BeginToolPress(position);
                return;
            }

            // a handle of the selection starts its drag at once
            if (TryBeginHandleDrag(position))
            {
                return;
            }

            // otherwise a tap, a move of objects, a move of the view or the menu - decided once it moves, lifts or stays
            _gesture = Gesture.Pending;
            var document = Document;
            var at = ToDocument(position);
            _pressHit = document is null ? null : ShapeHitTester.Find(document, at.X, at.Y, _zoom, FingerReach);
            _longPressTimer.Stop();
            _longPressTimer.Start();
            return;
        }

        // a second finger: zoom / move the view; an edit or drawing step that was under way is taken back
        _longPressTimer.Stop();
        if (_gesture is Gesture.Place or Gesture.PathPoint)
        {
            CancelToolPress();
        }
        else
        {
            CancelEdit();
        }

        _gesture = Gesture.View;
        BeginViewGesture();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pointers.ContainsKey(e.Pointer.Id))
        {
            return;
        }

        var position = e.GetPosition(this);
        _pointers[e.Pointer.Id] = position;
        e.Handled = true;

        // the menu came up while the finger rested, but the finger goes on: it was a drag after all - the menu goes, the move starts
        if (_gesture == Gesture.Menu)
        {
            var sinceMenu = position - _pressPoint;
            if (Math.Sqrt(sinceMenu.X * sinceMenu.X + sinceMenu.Y * sinceMenu.Y) < DragThreshold * 2)
            {
                return;
            }

            CloseMenu();
            _gesture = Gesture.Pending;
        }

        if (_gesture == Gesture.Pending)
        {
            var travelled = position - _pressPoint;
            if (Math.Sqrt(travelled.X * travelled.X + travelled.Y * travelled.Y) < DragThreshold)
            {
                return;
            }

            _longPressTimer.Stop();
            if (_pressHit is { } hit && !IsLocked(hit.Shape, hit.Layer) && BeginMove(hit.Shape, hit.Layer))
            {
                _gesture = Gesture.MoveShapes;
            }
            else
            {
                _gesture = Gesture.View;
                BeginViewGesture();
            }
        }

        switch (_gesture)
        {
            case Gesture.View:
                UpdateViewGesture();
                break;
            case Gesture.Place or Gesture.PathPoint:
                UpdateTool(position);
                break;
            default:
                UpdateEdit(position);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndPointer(e.Pointer.Id);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPointer(e.Pointer.Id);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var position = e.GetPosition(this);
        var zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), MinZoom, MaxZoom);
        var anchor = (position - _offset) / _zoom;
        SetView(zoom, position - anchor * zoom);
        e.Handled = true;
    }

    /// <summary>Double tap: on a text or a shape that can carry text - type its text (as a double click on Windows); on empty paper - fit the page.</summary>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var document = Document;
        if (document is null || ToolIsActive)
        {
            return;
        }

        var at = ToDocument(e.GetPosition(this));
        if (ShapeHitTester.Find(document, at.X, at.Y, _zoom, FingerReach) is not { } hit)
        {
            FitToView();
            return;
        }

        if (!IsLocked(hit.Shape, hit.Layer) && CanEditText(hit.Shape))
        {
            Select(hit.Shape, hit.Layer);
            RequestText(hit.Shape, hit.Layer, hit.Shape.X, hit.Shape.Y);
        }
    }

    /// <summary>True for a text object and for every shape that can carry text inside it.</summary>
    public static bool CanEditText(Shape shape) => shape.Kind == ShapeKind.Text || ShapeLabel.CanHaveLabel(shape);

    /// <summary>Raised when text should be typed: for a new text object (no shape), a text object or the text inside a shape.</summary>
    public event EventHandler<TextRequest>? TextRequested;

    private void RequestText(Shape? shape, Layer? layer, double x, double y) => TextRequested?.Invoke(this, new TextRequest(shape, layer, x, y));

    private void OnLongPress(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (_gesture != Gesture.Pending || _pointers.Count != 1 || _context is null)
        {
            return;
        }

        _gesture = Gesture.Menu;
        if (_pressHit is { } hit)
        {
            Select(hit.Shape, hit.Layer);
            _context.RequestRedraw();
        }

        ShowMenu(_pressHit is not null);
    }

    private void EndPointer(int id)
    {
        if (!_pointers.Remove(id))
        {
            return;
        }

        if (_pointers.Count == 0)
        {
            _longPressTimer.Stop();
            if (_gesture == Gesture.Pending)
            {
                Tap();
            }
            else if (_gesture == Gesture.TextTap)
            {
                var at = ToDocument(_pressPoint);
                RequestText(null, _context?.SelectedLayer ?? Document?.Layers.FirstOrDefault(), at.X, at.Y);
            }
            else if (_gesture is Gesture.Place or Gesture.PathPoint)
            {
                FinishToolPress();
            }
            else if (_gesture is not (Gesture.View or Gesture.Menu or Gesture.None))
            {
                FinishEdit();
            }

            _gesture = Gesture.None;
        }
        else if (_gesture == Gesture.View)
        {
            BeginViewGesture(); // the remaining fingers carry on from the current view without a jump
        }
    }

    // ----- view: pan and pinch

    private void BeginViewGesture()
    {
        (_startCentre, _startSpread) = CentreAndSpread();
        _startZoom = _zoom;
        _startOffset = _offset;
    }

    private void UpdateViewGesture()
    {
        var (centre, spread) = CentreAndSpread();

        // the drawing point that was under the fingers' centre stays under it; the spread of two fingers scales the zoom
        var zoom = _startSpread > 0 && spread > 0 ? Math.Clamp(_startZoom * spread / _startSpread, MinZoom, MaxZoom) : _startZoom;
        var anchor = (_startCentre - _startOffset) / _startZoom;
        SetView(zoom, centre - anchor * zoom);
    }

    /// <summary>The middle of the first two pointers and their distance (0 with one pointer).</summary>
    private (Point Centre, double Spread) CentreAndSpread()
    {
        if (_pointers.Count == 0)
        {
            return (default, 0);
        }

        using var points = _pointers.Values.GetEnumerator();
        points.MoveNext();
        var first = points.Current;
        if (!points.MoveNext())
        {
            return (first, 0);
        }

        var second = points.Current;
        var d = second - first;
        return (new Point((first.X + second.X) / 2, (first.Y + second.Y) / 2), Math.Sqrt(d.X * d.X + d.Y * d.Y));
    }
}
