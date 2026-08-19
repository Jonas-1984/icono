using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Icono.App.Views;

public partial class CanvasHostView : UserControl
{
    private const double MinShapeSize = 4;
    private const double HandleHitToleranceDip = 6.0;
    private const float HandleScreenSize = 7f;
    private static readonly SKColor AccentColor = new(0x6F, 0xB1, 0xFF);

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(CanvasHostView),
        new PropertyMetadata(1.0, OnZoomChanged));

    private EditorContext? _context;

    private bool _isPanning;
    private Point _panStart;

    private bool _isPlacing;
    private Point _placeStart;
    private Shape? _pendingShape;

    private bool _isMovingSelection;
    private Point _moveLastPoint;

    private bool _isResizing;
    private ResizeHandle _resizeHandle;
    private Shape? _resizeShape;
    private (double X, double Y, double W, double H) _resizeStartRect;
    private Point _resizeStartPoint;

    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Point _marqueeCurrent;

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

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

            UpdateCanvasFrameSize();
            SkiaCanvas.InvalidateVisual();
        }
    }

    public CanvasHostView()
    {
        InitializeComponent();

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseLeave += OnMouseLeave;
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CanvasHostView)d).UpdateCanvasFrameSize();

    private void UpdateCanvasFrameSize()
    {
        if (_context is null)
        {
            return;
        }

        CanvasFrame.Width = _context.Document.Width * Zoom;
        CanvasFrame.Height = _context.Document.Height * Zoom;
    }

    /// <summary>Converts a point in this control's own DIP space (e.g. from
    /// <c>e.GetPosition(SkiaCanvas)</c>) into document-space coordinates. Distinct from the
    /// Skia-internal raster scale used only inside <see cref="SkiaCanvas_OnPaintSurface"/>.</summary>
    private Point ToDocumentSpace(Point viewPoint) => new(viewPoint.X / Zoom, viewPoint.Y / Zoom);

    private void OnContextInvalidated(object? sender, EventArgs e) => SkiaCanvas.InvalidateVisual();

    private void SkiaCanvas_OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;

        if (_context is null)
        {
            CheckerboardRenderer.Render(canvas, e.Info.Width, e.Info.Height);
            return;
        }

        var document = _context.Document;
        var scaleX = document.Width > 0 ? e.Info.Width / (float)document.Width : 1f;
        var scaleY = document.Height > 0 ? e.Info.Height / (float)document.Height : 1f;

        canvas.Save();
        canvas.Scale(scaleX, scaleY);
        DocumentRenderer.Render(canvas, document, document.Width, document.Height, _pendingShape);
        canvas.Restore();

        // Chrome below is drawn in raw raster space (post-Restore) so it stays a constant
        // screen size regardless of zoom.
        DrawSelectionChrome(canvas, scaleX, scaleY);
    }

    private void DrawSelectionChrome(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_context is null)
        {
            return;
        }

        var selected = _context.SelectedShapes;

        foreach (var shape in selected)
        {
            DrawSelectionOutline(canvas, shape, scaleX, scaleY);
        }

        if (selected.Count == 1)
        {
            DrawResizeHandles(canvas, selected[0], scaleX, scaleY);
        }

        if (_isMarqueeSelecting)
        {
            DrawMarquee(canvas, scaleX, scaleY);
        }
    }

    private static void DrawSelectionOutline(SKCanvas canvas, Shape shape, float scaleX, float scaleY)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = AccentColor,
            PathEffect = SKPathEffect.CreateDash([4, 3], 0),
        };

        var rect = SKRect.Create(
            (float)shape.X * scaleX - 3, (float)shape.Y * scaleY - 3,
            (float)shape.Width * scaleX + 6, (float)shape.Height * scaleY + 6);
        canvas.DrawRect(rect, paint);
    }

    private void DrawResizeHandles(SKCanvas canvas, Shape shape, float scaleX, float scaleY)
    {
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var strokePaint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = AccentColor,
        };

        foreach (var (_, x, y) in EnumerateHandlePositions(shape))
        {
            var rect = SKRect.Create(
                (float)x * scaleX - HandleScreenSize / 2, (float)y * scaleY - HandleScreenSize / 2,
                HandleScreenSize, HandleScreenSize);
            canvas.DrawRect(rect, fillPaint);
            canvas.DrawRect(rect, strokePaint);
        }
    }

    private void DrawMarquee(SKCanvas canvas, float scaleX, float scaleY)
    {
        var rect = BuildMarqueeDocRect();

        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = AccentColor.WithAlpha(40) };
        using var strokePaint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = AccentColor,
            PathEffect = SKPathEffect.CreateDash([4, 3], 0),
        };

        var screenRect = SKRect.Create(
            rect.Left * scaleX, rect.Top * scaleY, rect.Width * scaleX, rect.Height * scaleY);
        canvas.DrawRect(screenRect, fillPaint);
        canvas.DrawRect(screenRect, strokePaint);
    }

    private static IEnumerable<(ResizeHandle Handle, double X, double Y)> EnumerateHandlePositions(Shape shape)
    {
        var left = shape.X;
        var top = shape.Y;
        var right = shape.X + shape.Width;
        var bottom = shape.Y + shape.Height;
        var midX = shape.X + shape.Width / 2;
        var midY = shape.Y + shape.Height / 2;

        yield return (ResizeHandle.TopLeft, left, top);
        yield return (ResizeHandle.Top, midX, top);
        yield return (ResizeHandle.TopRight, right, top);
        yield return (ResizeHandle.Right, right, midY);
        yield return (ResizeHandle.BottomRight, right, bottom);
        yield return (ResizeHandle.Bottom, midX, bottom);
        yield return (ResizeHandle.BottomLeft, left, bottom);
        yield return (ResizeHandle.Left, left, midY);
    }

    private ResizeHandle? HitTestHandle(Point docPoint, Shape shape)
    {
        var tolerance = HandleHitToleranceDip / Zoom;

        foreach (var (handle, x, y) in EnumerateHandlePositions(shape))
        {
            if (Math.Abs(docPoint.X - x) <= tolerance && Math.Abs(docPoint.Y - y) <= tolerance)
            {
                return handle;
            }
        }

        return null;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            BeginPan(e);
            return;
        }

        if (e.ChangedButton != MouseButton.Left || _context is null)
        {
            return;
        }

        if (_context.ActiveTool == ActiveTool.PanZoom)
        {
            BeginPan(e);
            return;
        }

        var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));

        if (_context.ActiveTool == ActiveTool.Shape)
        {
            BeginPlacing(point);
            return;
        }

        if (_context.ActiveTool == ActiveTool.PaintBucket)
        {
            HandlePaintBucketClick(point);
            return;
        }

        // Select tool.
        var ctrlHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (_context.SelectedShapes.Count == 1 && HitTestHandle(point, _context.SelectedShapes[0]) is { } handle)
        {
            BeginResize(handle, _context.SelectedShapes[0], point);
            return;
        }

        var hit = HitTestShape(point);
        if (hit is not null)
        {
            var (shape, layer) = hit.Value;

            if (ctrlHeld)
            {
                if (!_context.SelectedShapes.Remove(shape))
                {
                    _context.SelectedShapes.Add(shape);
                }

                _context.SelectedLayer = layer;
                _context.RequestRedraw();
                return;
            }

            if (!_context.SelectedShapes.Contains(shape))
            {
                _context.SelectedShape = shape;
            }

            _context.SelectedLayer = layer;
            _isMovingSelection = true;
            _moveLastPoint = point;
            CaptureMouse();
            _context.RequestRedraw();
            return;
        }

        if (!ctrlHeld)
        {
            _context.SelectedShapes.Clear();
        }

        _isMarqueeSelecting = true;
        _marqueeStart = point;
        _marqueeCurrent = point;
        CaptureMouse();
        _context.RequestRedraw();
    }

    private (Shape Shape, Layer Layer)? HitTestShape(Point point)
    {
        if (_context is null)
        {
            return null;
        }

        // Front-to-back: index 0 = front-most, in both layer and shape lists.
        foreach (var layer in _context.Document.Layers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            foreach (var shape in layer.Shapes)
            {
                if (point.X < shape.X || point.X > shape.X + shape.Width ||
                    point.Y < shape.Y || point.Y > shape.Y + shape.Height)
                {
                    continue;
                }

                return (shape, layer);
            }
        }

        return null;
    }

    private void BeginPlacing(Point point)
    {
        if (_context is null)
        {
            return;
        }

        _isPlacing = true;
        _placeStart = point;
        _pendingShape = new Shape
        {
            Kind = _context.SelectedShapeKind,
            X = point.X,
            Y = point.Y,
            Width = 0,
            Height = 0,
            FillColor = _context.CurrentFillColor,
        };
        CaptureMouse();
    }

    private void BeginResize(ResizeHandle handle, Shape shape, Point point)
    {
        _isResizing = true;
        _resizeHandle = handle;
        _resizeShape = shape;
        _resizeStartRect = (shape.X, shape.Y, shape.Width, shape.Height);
        _resizeStartPoint = point;
        CaptureMouse();
    }

    private void HandlePaintBucketClick(Point point)
    {
        if (_context is null)
        {
            return;
        }

        var hit = HitTestShape(point);
        if (hit is null)
        {
            return;
        }

        var (shape, layer) = hit.Value;

        var distanceToEdge = Math.Min(
            Math.Min(point.X - shape.X, shape.X + shape.Width - point.X),
            Math.Min(point.Y - shape.Y, shape.Y + shape.Height - point.Y));

        const double toleranceDocPx = 3.0;
        var borderBand = shape.BorderThickness / 2.0 + toleranceDocPx;

        if (shape.BorderThickness > 0 && distanceToEdge <= borderBand)
        {
            shape.BorderColor = _context.CurrentFillColor;
        }
        else
        {
            shape.FillColor = _context.CurrentFillColor;
        }

        _context.SelectedShape = shape;
        _context.SelectedLayer = layer;
        _context.RequestRedraw();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var screenPoint = e.GetPosition(this);
            PanTransform.X += screenPoint.X - _panStart.X;
            PanTransform.Y += screenPoint.Y - _panStart.Y;
            _panStart = screenPoint;
            return;
        }

        if (_isPlacing && _pendingShape is not null)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            _pendingShape.X = Math.Min(_placeStart.X, point.X);
            _pendingShape.Y = Math.Min(_placeStart.Y, point.Y);
            _pendingShape.Width = Math.Abs(point.X - _placeStart.X);
            _pendingShape.Height = Math.Abs(point.Y - _placeStart.Y);
            _context?.RequestRedraw();
            return;
        }

        if (_isResizing && _resizeShape is not null)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            ApplyResize(point);
            _context?.RequestRedraw();
            return;
        }

        if (_isMovingSelection && _context is not null)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            var dx = point.X - _moveLastPoint.X;
            var dy = point.Y - _moveLastPoint.Y;

            foreach (var shape in _context.SelectedShapes)
            {
                shape.X += dx;
                shape.Y += dy;
            }

            _moveLastPoint = point;
            _context.RequestRedraw();
            return;
        }

        if (_isMarqueeSelecting)
        {
            _marqueeCurrent = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            _context?.RequestRedraw();
        }
    }

    private void ApplyResize(Point point)
    {
        if (_resizeShape is null)
        {
            return;
        }

        var dx = point.X - _resizeStartPoint.X;
        var dy = point.Y - _resizeStartPoint.Y;
        var (startX, startY, startW, startH) = _resizeStartRect;

        double newX = startX, newY = startY, newW = startW, newH = startH;

        var affectsLeft = _resizeHandle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        var affectsRight = _resizeHandle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        var affectsTop = _resizeHandle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        var affectsBottom = _resizeHandle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;

        if (affectsLeft)
        {
            newW = Math.Max(MinShapeSize, startW - dx);
            newX = startX + (startW - newW);
        }
        else if (affectsRight)
        {
            newW = Math.Max(MinShapeSize, startW + dx);
        }

        if (affectsTop)
        {
            newH = Math.Max(MinShapeSize, startH - dy);
            newY = startY + (startH - newH);
        }
        else if (affectsBottom)
        {
            newH = Math.Max(MinShapeSize, startH + dy);
        }

        _resizeShape.X = newX;
        _resizeShape.Y = newY;
        _resizeShape.Width = newW;
        _resizeShape.Height = newH;
    }

    private SKRect BuildMarqueeDocRect()
    {
        var x = Math.Min(_marqueeStart.X, _marqueeCurrent.X);
        var y = Math.Min(_marqueeStart.Y, _marqueeCurrent.Y);
        var w = Math.Abs(_marqueeCurrent.X - _marqueeStart.X);
        var h = Math.Abs(_marqueeCurrent.Y - _marqueeStart.Y);
        return SKRect.Create((float)x, (float)y, (float)w, (float)h);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            EndPan();
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_isPanning)
        {
            EndPan();
            return;
        }

        if (_isPlacing)
        {
            CommitPendingShape();
            return;
        }

        if (_isResizing)
        {
            _isResizing = false;
            _resizeShape = null;
            ReleaseMouseCapture();
            return;
        }

        if (_isMovingSelection)
        {
            _isMovingSelection = false;
            ReleaseMouseCapture();
            return;
        }

        if (_isMarqueeSelecting)
        {
            CommitMarqueeSelection();
        }
    }

    private void CommitMarqueeSelection()
    {
        _isMarqueeSelecting = false;
        ReleaseMouseCapture();

        if (_context is null)
        {
            return;
        }

        var rect = BuildMarqueeDocRect();

        foreach (var layer in _context.Document.Layers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            foreach (var shape in layer.Shapes)
            {
                if (Intersects(shape, rect) && !_context.SelectedShapes.Contains(shape))
                {
                    _context.SelectedShapes.Add(shape);
                }
            }
        }

        _context.RequestRedraw();
    }

    private static bool Intersects(Shape shape, SKRect rect) =>
        shape.X < rect.Right && shape.X + shape.Width > rect.Left &&
        shape.Y < rect.Bottom && shape.Y + shape.Height > rect.Top;

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            EndPan();
        }
    }

    private void BeginPan(MouseButtonEventArgs e)
    {
        _isPanning = true;
        _panStart = e.GetPosition(this);
        CaptureMouse();
        Cursor = Cursors.Hand;
    }

    private void EndPan()
    {
        _isPanning = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void CommitPendingShape()
    {
        _isPlacing = false;
        ReleaseMouseCapture();

        if (_pendingShape is { Width: >= 2, Height: >= 2 } shape && _context is not null)
        {
            var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
            if (layer is not null)
            {
                layer.Shapes.Insert(0, shape);
                _context.SelectedLayer = layer;
                _context.SelectedShape = shape;
            }
        }

        _pendingShape = null;
        _context?.RequestRedraw();
    }
}
