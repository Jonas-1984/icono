using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>Shows a <see cref="Document"/> with the same renderer as the Windows program. Touch: one finger moves the drawing, two
/// fingers zoom and move it at once, a double tap fits the page into the view. Mouse (for testing on a PC): drag moves, the wheel zooms.</summary>
public sealed class DocumentCanvas : Control
{
    public const double MinZoom = 0.05;
    public const double MaxZoom = 40;

    /// <summary>Space round the page when it is fitted into the view (device-independent pixels).</summary>
    private const double FitMargin = 24;

    private Document? _document;
    private double _zoom = 1;
    private Vector _offset;
    private bool _fitPending = true;

    // the fingers (or the mouse) on the canvas and the view when the current gesture started
    private readonly Dictionary<int, Point> _pointers = new();
    private double _startZoom;
    private Vector _startOffset;
    private Point _startCentre;
    private double _startSpread;

    public DocumentCanvas()
    {
        Focusable = true;
        DoubleTapped += (_, _) => FitToView();
    }

    /// <summary>Raised when <see cref="Zoom"/> changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Screen pixels (DIP) per drawing unit.</summary>
    public double Zoom => _zoom;

    public Document? Document
    {
        get => _document;
        set
        {
            _document = value;
            _fitPending = true;
            FitToView();
        }
    }

    /// <summary>Zooms so that the whole width of the page, and the first page in full, are visible and centres it.</summary>
    public void FitToView()
    {
        if (_document is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return; // done on the first layout pass instead
        }

        _fitPending = false;
        var availableWidth = Math.Max(1, Bounds.Width - 2 * FitMargin);
        var availableHeight = Math.Max(1, Bounds.Height - 2 * FitMargin);
        var zoom = Math.Min(availableWidth / _document.Width, availableHeight / _document.Height);
        SetView(zoom, new Vector(
            (Bounds.Width - _document.Width * zoom) / 2,
            Math.Max(FitMargin, (Bounds.Height - _document.Height * zoom) / 2)));
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
        _pointers[e.Pointer.Id] = e.GetPosition(this);
        e.Pointer.Capture(this);
        BeginGesture();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pointers.ContainsKey(e.Pointer.Id))
        {
            return;
        }

        _pointers[e.Pointer.Id] = e.GetPosition(this);
        var (centre, spread) = CentreAndSpread();

        // the drawing point that was under the fingers' centre stays under it; the spread of two fingers scales the zoom
        var zoom = _startSpread > 0 && spread > 0 ? Math.Clamp(_startZoom * spread / _startSpread, MinZoom, MaxZoom) : _startZoom;
        var anchor = (_startCentre - _startOffset) / _startZoom;
        SetView(zoom, centre - anchor * zoom);
        e.Handled = true;
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

    private void EndPointer(int id)
    {
        if (_pointers.Remove(id))
        {
            BeginGesture(); // the remaining fingers carry on from the current view without a jump
        }
    }

    private void BeginGesture()
    {
        (_startCentre, _startSpread) = CentreAndSpread();
        _startZoom = _zoom;
        _startOffset = _offset;
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
        if (_document is not null)
        {
            context.Custom(new DrawOperation(new Rect(Bounds.Size), _document, _zoom, _offset));
        }
    }

    /// <summary>Draws on the render thread with Avalonia's own Skia canvas; it only reads the document.</summary>
    private sealed class DrawOperation(Rect bounds, Document document, double zoom, Vector offset) : ICustomDrawOperation
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
        }
    }
}
