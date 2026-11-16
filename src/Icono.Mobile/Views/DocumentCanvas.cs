using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Icono.Core;
using Icono.Editor;
using Icono.Editor.Undo;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>Shows and edits the document of an <see cref="EditorContext"/> with the same renderer as the Windows program.
/// <list type="bullet">
/// <item>tap an object: select it (a group as a whole); tap empty paper: select nothing</item>
/// <item>drag an object: move it (with the selection it belongs to) - one undo step</item>
/// <item>drag empty paper: move the view; two fingers: zoom and move the view; double tap on empty paper: fit the page</item>
/// </list>
/// A mouse (testing on a PC) works like one finger; the wheel zooms.</summary>
public sealed class DocumentCanvas : Control
{
    public const double MinZoom = 0.05;
    public const double MaxZoom = 40;

    /// <summary>Space round the page when it is fitted into the view (device-independent pixels).</summary>
    private const double FitMargin = 24;

    /// <summary>How far a finger may wander before a touch counts as a drag instead of a tap (DIP).</summary>
    private const double DragThreshold = 8;

    /// <summary>How far beside thin objects (dimension lines, walls, doors) a finger still hits them (DIP).</summary>
    private const double FingerReach = 12;

    private static readonly SKColor SelectionColor = new(0x5A, 0xB0, 0xFF);
    private static readonly SKColor LockedColor = new(0x90, 0x90, 0x90);

    private EditorContext? _context;
    private double _zoom = 1;
    private Vector _offset;
    private bool _fitPending = true;

    private enum Gesture { None, Pending, MoveShapes, View }

    private Gesture _gesture;

    // the fingers (or the mouse) on the canvas
    private readonly Dictionary<int, Point> _pointers = new();

    // view gesture: the view when it started
    private double _startZoom;
    private Vector _startOffset;
    private Point _startCentre;
    private double _startSpread;

    // tap / move gesture
    private Point _pressPoint;
    private (Shape Shape, Layer Layer)? _pressHit;
    private List<(double X, double Y)> _moveStart = [];
    private List<ShapeSnapshot> _moveBefore = [];
    private Point _moveStartDocument;

    public DocumentCanvas()
    {
        Focusable = true;
        DoubleTapped += OnDoubleTapped;
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
            // one finger: a tap, a move of objects or a move of the view - decided once it moves or lifts
            _gesture = Gesture.Pending;
            _pressPoint = position;
            var document = Document;
            var at = ToDocument(position);
            _pressHit = document is null ? null : ShapeHitTester.Find(document, at.X, at.Y, _zoom, FingerReach);
            return;
        }

        // a second finger: zoom / move the view; an object move that was under way is taken back
        CancelMove();
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

        if (_gesture == Gesture.Pending)
        {
            var travelled = position - _pressPoint;
            if (Math.Sqrt(travelled.X * travelled.X + travelled.Y * travelled.Y) < DragThreshold)
            {
                return;
            }

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
            case Gesture.MoveShapes:
                MoveShapes(position);
                break;
            case Gesture.View:
                UpdateViewGesture();
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

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var document = Document;
        var at = ToDocument(e.GetPosition(this));
        if (document is not null && ShapeHitTester.Find(document, at.X, at.Y, _zoom, FingerReach) is null)
        {
            FitToView();
        }
    }

    private void EndPointer(int id)
    {
        if (!_pointers.Remove(id))
        {
            return;
        }

        switch (_gesture)
        {
            case Gesture.Pending when _pointers.Count == 0:
                Tap();
                break;
            case Gesture.MoveShapes when _pointers.Count == 0:
                FinishMove();
                break;
        }

        if (_pointers.Count == 0)
        {
            _gesture = Gesture.None;
        }
        else if (_gesture == Gesture.View)
        {
            BeginViewGesture(); // the remaining fingers carry on from the current view without a jump
        }
    }

    // ----- tap: select

    private void Tap()
    {
        if (_context is null)
        {
            return;
        }

        if (_pressHit is { } hit)
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

    private bool IsLocked(Shape shape, Layer layer) => shape.Locked || layer.Locked;

    // ----- drag: move the selection

    private bool BeginMove(Shape shape, Layer layer)
    {
        if (_context is null)
        {
            return false;
        }

        Select(shape, layer);
        if (_context.SelectedShapes.Any(s => s.Locked))
        {
            return false;
        }

        _moveStartDocument = ToDocument(_pressPoint);
        _moveStart = _context.SelectedShapes.Select(s => (s.X, s.Y)).ToList();
        _moveBefore = _context.SelectedShapes.Select(ShapeSnapshot.Capture).ToList();
        return true;
    }

    private void MoveShapes(Point position)
    {
        if (_context is null || _moveStart.Count != _context.SelectedShapes.Count)
        {
            return;
        }

        // from the frozen start, not step by step, so nothing drifts
        var now = ToDocument(position);
        var dx = now.X - _moveStartDocument.X;
        var dy = now.Y - _moveStartDocument.Y;
        for (var i = 0; i < _moveStart.Count; i++)
        {
            _context.SelectedShapes[i].X = _moveStart[i].X + dx;
            _context.SelectedShapes[i].Y = _moveStart[i].Y + dy;
        }

        _context.RequestRedraw();
    }

    private void FinishMove()
    {
        if (_context is null || _moveBefore.Count != _context.SelectedShapes.Count)
        {
            return;
        }

        var shapes = _context.SelectedShapes.ToList();
        var after = shapes.Select(ShapeSnapshot.Capture).ToList();
        _context.UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, _moveBefore, after));
        _moveBefore = [];
        _moveStart = [];
        _context.RequestRedraw();
    }

    private void CancelMove()
    {
        if (_gesture != Gesture.MoveShapes || _context is null || _moveBefore.Count != _context.SelectedShapes.Count)
        {
            return;
        }

        for (var i = 0; i < _moveBefore.Count; i++)
        {
            _moveBefore[i].ApplyTo(_context.SelectedShapes[i]);
        }

        _moveBefore = [];
        _moveStart = [];
        _context.RequestRedraw();
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

    // ---------------------------------------------------------------- drawing

    public override void Render(DrawingContext context)
    {
        if (_context is null)
        {
            return;
        }

        // the selection frames are worked out here (UI thread); the render thread only draws them
        var frames = _context.SelectedShapes.Select(s => new SelectionFrame(FrameCorners(s), s.Locked)).ToList();
        context.Custom(new DrawOperation(new Rect(Bounds.Size), _context.Document, _zoom, _offset, frames));
    }

    private readonly record struct SelectionFrame(SKPoint[] Corners, bool Locked);

    /// <summary>The corners of the frame round a shape in drawing units (turned with it), as the Windows program draws it.</summary>
    private static SKPoint[] FrameCorners(Shape shape)
    {
        var box = ShapeOutline.Box(shape);
        var pad = Math.Max(0, shape.BorderThickness / 2);
        var corners = new[]
        {
            (box.X - pad, box.Y - pad), (box.X + box.W + pad, box.Y - pad),
            (box.X + box.W + pad, box.Y + box.H + pad), (box.X - pad, box.Y + box.H + pad),
        };

        var cx = shape.X + shape.Width / 2;
        var cy = shape.Y + shape.Height / 2;
        var rad = shape.Rotation * Math.PI / 180.0;
        var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
        return corners.Select(c =>
        {
            if (shape.Rotation == 0)
            {
                return new SKPoint((float)c.Item1, (float)c.Item2);
            }

            var (dx, dy) = (c.Item1 - cx, c.Item2 - cy);
            return new SKPoint((float)(cx + dx * cos - dy * sin), (float)(cy + dx * sin + dy * cos));
        }).ToArray();
    }

    /// <summary>Draws on the render thread with Avalonia's own Skia canvas.</summary>
    private sealed class DrawOperation(Rect bounds, Document document, double zoom, Vector offset, IReadOnlyList<SelectionFrame> frames)
        : ICustomDrawOperation
    {
        private static readonly SKColor PageShadow = new(0, 0, 0, 140);

        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null)
            {
                return;
            }

            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, 0, (float)bounds.Width, (float)bounds.Height));
            canvas.Save();
            canvas.Translate((float)offset.X, (float)offset.Y);
            canvas.Scale((float)zoom);

            using (var shadow = new SKPaint
                   {
                       Color = PageShadow,
                       IsAntialias = true,
                       MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(8 / zoom)),
                   })
            {
                for (var page = 0; page < document.PageCount; page++)
                {
                    canvas.DrawRect(SKRect.Create(0, document.PageTop(page) + (float)(3 / zoom), document.Width, document.Height), shadow);
                }
            }

            DocumentRenderer.Render(canvas, document, document.Width, document.Height);
            canvas.Restore();

            // selection frames in screen units, so they keep their width at every zoom
            DrawFrames(canvas);
            canvas.Restore();
        }

        private void DrawFrames(SKCanvas canvas)
        {
            if (frames.Count == 0)
            {
                return;
            }

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                PathEffect = SKPathEffect.CreateDash([6, 4], 0),
            };

            foreach (var frame in frames)
            {
                paint.Color = frame.Locked ? LockedColor : SelectionColor;
                var points = frame.Corners
                    .Select(c => new SKPoint((float)(c.X * zoom + offset.X), (float)(c.Y * zoom + offset.Y)))
                    .ToArray();
                for (var i = 0; i < points.Length; i++)
                {
                    canvas.DrawLine(points[i], points[(i + 1) % points.Length], paint);
                }
            }
        }
    }
}
