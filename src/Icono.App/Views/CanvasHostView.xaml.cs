using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Icono.App.State;
using Icono.App.State.Undo;
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
    private List<ShapeSnapshot>? _moveUndoBefore;

    private bool _isResizing;
    private ResizeHandle _resizeHandle;
    private Shape? _resizeShape;
    private (double X, double Y, double W, double H) _resizeStartRect;
    private Point _resizeStartPoint;
    private Point _resizeCenter;
    private double _resizeRotation;
    private ShapeSnapshot _resizeUndoBefore;

    private bool _isRotating;
    private Shape? _rotateShape;
    private ShapeSnapshot _rotateUndoBefore;

    private bool _isDraggingDepth;
    private Shape? _depthDragShape;
    private ShapeSnapshot _depthDragUndoBefore;

    private bool _isPathDrawing;
    private Shape? _pendingPathShape;

    private bool _isDraggingPoint;
    private Shape? _pointEditShape;
    private int? _draggedPointIndex;
    private ShapeSnapshot _pointDragUndoBefore;

    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Point _marqueeCurrent;

    private TextBox? _textEditBox;
    private Shape? _textEditShape;
    private Layer? _textEditLayer;
    private Point _textEditOrigin;

    private static readonly HashSet<ShapeKind> TwoPointLineKinds = [ShapeKind.Line, ShapeKind.Arrow, ShapeKind.DoubleArrow];

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
        MouseRightButtonDown += OnMouseRightButtonDown;
    }

    /// <summary>Right-click: select the shape under the cursor (if any, and not already part of
    /// the selection) without starting a drag, then show the shape/edit context menu.</summary>
    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
        var hit = HitTestShape(point);

        if (hit is not null && !_context.SelectedShapes.Contains(hit.Value.Shape))
        {
            _context.SelectedShape = hit.Value.Shape;
            _context.SelectedLayer = hit.Value.Layer;
            _context.RequestRedraw();
        }

        ShowContextMenu();
    }

    /// <summary>Builds and opens a context menu reflecting the current selection — consolidates
    /// Cut/Copy/Paste/Union/Trim/Group/Lock/z-order onto the shape(s) directly, per the user's
    /// request for right-click shape editing.</summary>
    private void ShowContextMenu()
    {
        if (_context is null)
        {
            return;
        }

        var selected = _context.SelectedShapes.ToList();
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };

        void AddItem(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        AddItem("Cut", _context.CutSelection, selected.Count > 0 && selected.Any(s => !IsLocked(s)));
        AddItem("Copy", _context.CopySelection, selected.Count > 0);
        AddItem("Paste", _context.PasteClipboard, _context.Clipboard.Count > 0);
        AddItem("Delete", _context.DeleteSelection, selected.Count > 0 && selected.Any(s => !IsLocked(s)));
        menu.Items.Add(new Separator());

        AddItem("Union", _context.UnionSelection, selected.Count >= 2);
        AddItem("Trim (first selected = cutter)", _context.TrimSelection, selected.Count == 2);

        if (selected.Count == 1 && selected[0].Points is null && ShapeGeometryFactory.SupportsPointEditing(selected[0].Kind))
        {
            var shape = selected[0];
            AddItem("Edit Points", () => _context.ConvertToEditablePoints(shape));
        }

        menu.Items.Add(new Separator());

        AddItem("Group", _context.GroupSelection, selected.Count >= 2);
        AddItem("Ungroup", _context.UngroupSelection, selected.Any(s => s.GroupId is not null));

        var allLocked = selected.Count > 0 && selected.All(s => s.Locked);
        AddItem(allLocked ? "Unlock" : "Lock", () => _context.SetSelectionLocked(!allLocked), selected.Count > 0);

        menu.Items.Add(new Separator());
        AddItem("Bring to Front", _context.BringSelectionToFront, selected.Count > 0);
        AddItem("Send to Back", _context.SendSelectionToBack, selected.Count > 0);

        menu.IsOpen = true;
    }

    /// <summary>Called from MainWindow's window-level PreviewKeyDown (Escape), since this control
    /// isn't guaranteed to hold keyboard focus while the user is clicking to place path points.</summary>
    public void CancelPathDraw()
    {
        if (!_isPathDrawing)
        {
            return;
        }

        _isPathDrawing = false;
        _pendingPathShape = null;
        _context?.RequestRedraw();
    }

    /// <summary>Called from MainWindow's window-level PreviewKeyDown (Enter).</summary>
    public void FinishPathDraw()
    {
        if (!_isPathDrawing || _pendingPathShape is null || _context is null)
        {
            _isPathDrawing = false;
            return;
        }

        var shape = _pendingPathShape;
        var points = shape.Points!.ToList();
        if (points.Count > 1)
        {
            // Drop the trailing ghost point — it only ever tracked the mouse, never a real click.
            points.RemoveAt(points.Count - 1);
        }

        _isPathDrawing = false;
        _pendingPathShape = null;

        if (points.Count < 2)
        {
            _context.RequestRedraw();
            return;
        }

        shape.Points = points;
        PathBoundsCalculator.Recompute(shape);

        var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
        if (layer is not null)
        {
            layer.Shapes.Insert(0, shape);
            _context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
            _context.SelectedLayer = layer;
            _context.SelectedShape = shape;
        }

        _context.RequestRedraw();
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
        DocumentRenderer.Render(canvas, document, document.Width, document.Height, _pendingShape ?? _pendingPathShape);

        if (_context.ShowGrid)
        {
            DrawGrid(canvas, document.Width, document.Height);
        }

        canvas.Restore();

        // Chrome below is drawn in raw raster space (post-Restore) so it stays a constant
        // screen size regardless of zoom.
        DrawSelectionChrome(canvas, scaleX, scaleY);
    }

    private void DrawGrid(SKCanvas canvas, int width, int height)
    {
        if (_context is null || _context.GridSize <= 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = new SKColor(255, 255, 255, 40),
        };

        var size = (float)_context.GridSize;

        for (var x = size; x < width; x += size)
        {
            canvas.DrawLine(x, 0, x, height, paint);
        }

        for (var y = size; y < height; y += size)
        {
            canvas.DrawLine(0, y, width, y, paint);
        }
    }

    /// <summary>Rounds to the nearest grid line when snap is on; a no-op otherwise. Applied at
    /// every place/resize/move/point-drag coordinate write.</summary>
    private double SnapIfEnabled(double value)
    {
        if (_context is not { SnapToGrid: true, GridSize: > 0 })
        {
            return value;
        }

        return Math.Round(value / _context.GridSize) * _context.GridSize;
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
            DrawSelectionOutline(canvas, shape, scaleX, scaleY, IsLocked(shape));
        }

        if (selected.Count == 1 && !IsLocked(selected[0]))
        {
            DrawResizeHandles(canvas, selected[0], scaleX, scaleY);
        }

        if (_isMarqueeSelecting)
        {
            DrawMarquee(canvas, scaleX, scaleY);
        }
    }

    private static void DrawSelectionOutline(SKCanvas canvas, Shape shape, float scaleX, float scaleY, bool locked = false)
    {
        // Locked shapes get a muted gray dashed outline instead of the accent blue, so it's
        // visually obvious why dragging/resizing them doesn't do anything.
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = locked ? new SKColor(0x90, 0x90, 0x90) : AccentColor,
            PathEffect = SKPathEffect.CreateDash([4, 3], 0),
        };

        var rect = SKRect.Create(
            (float)shape.X * scaleX - 3, (float)shape.Y * scaleY - 3,
            (float)shape.Width * scaleX + 6, (float)shape.Height * scaleY + 6);
        canvas.DrawRect(rect, paint);
    }

    private void DrawResizeHandles(SKCanvas canvas, Shape shape, float scaleX, float scaleY)
    {
        if (shape.Points is { Count: > 0 })
        {
            DrawPointHandles(canvas, shape, scaleX, scaleY);
            return;
        }

        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var strokePaint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = AccentColor,
        };

        var topHandle = EnumerateHandlePositions(shape).First(h => h.Handle == ResizeHandle.Top);
        var (rotX, rotY) = ComputeRotateHandlePosition(shape);

        canvas.DrawLine(
            (float)topHandle.X * scaleX, (float)topHandle.Y * scaleY,
            (float)rotX * scaleX, (float)rotY * scaleY,
            strokePaint);

        foreach (var (_, x, y) in EnumerateHandlePositions(shape))
        {
            var rect = SKRect.Create(
                (float)x * scaleX - HandleScreenSize / 2, (float)y * scaleY - HandleScreenSize / 2,
                HandleScreenSize, HandleScreenSize);
            canvas.DrawRect(rect, fillPaint);
            canvas.DrawRect(rect, strokePaint);
        }

        canvas.DrawCircle((float)rotX * scaleX, (float)rotY * scaleY, HandleScreenSize / 2, fillPaint);
        canvas.DrawCircle((float)rotX * scaleX, (float)rotY * scaleY, HandleScreenSize / 2, strokePaint);

        if (_isRotating && ReferenceEquals(_rotateShape, shape))
        {
            DrawRotationLabel(canvas, shape, rotX, rotY, scaleX, scaleY);
        }

        if (shape.Kind == ShapeKind.Box3D)
        {
            DrawDepthHandle(canvas, shape, scaleX, scaleY);
        }
    }

    /// <summary>The 3D-box's depth control — a distinct orange handle (vs. the white resize
    /// handles) sitting at the midpoint of the front-face-to-back-corner diagonal.</summary>
    private static void DrawDepthHandle(SKCanvas canvas, Shape shape, float scaleX, float scaleY)
    {
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xA0, 0x00) };
        using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = SKColors.White };

        var (x, y) = ComputeDepthHandlePosition(shape);
        var screenX = (float)x * scaleX;
        var screenY = (float)y * scaleY;

        canvas.DrawCircle(screenX, screenY, HandleScreenSize / 2, fillPaint);
        canvas.DrawCircle(screenX, screenY, HandleScreenSize / 2, strokePaint);
    }

    private static (double X, double Y) ComputeDepthHandlePosition(Shape shape)
    {
        var d = Math.Min(shape.Depth3D, Math.Min(shape.Width, shape.Height) * 0.6);
        return (shape.X + d / 2, shape.Y + d / 2);
    }

    private bool HitTestDepthHandle(Point docPoint, Shape shape)
    {
        var tolerance = HandleHitToleranceDip / Zoom;
        var (x, y) = ComputeDepthHandlePosition(shape);
        return Math.Abs(docPoint.X - x) <= tolerance && Math.Abs(docPoint.Y - y) <= tolerance;
    }

    private static void DrawRotationLabel(SKCanvas canvas, Shape shape, double rotX, double rotY, float scaleX, float scaleY)
    {
        using var font = new SKFont { Size = 12f };
        using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        using var bgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 170), Style = SKPaintStyle.Fill };

        var text = $"{shape.Rotation:0}°";
        var bounds = new SKRect();
        font.MeasureText(text, out bounds, textPaint);

        var screenX = (float)rotX * scaleX + 10;
        var screenY = (float)rotY * scaleY - 6;
        const float pad = 4f;

        var bgRect = SKRect.Create(
            screenX - pad, screenY + bounds.Top - pad, bounds.Width + pad * 2, bounds.Height + pad * 2);
        canvas.DrawRoundRect(bgRect, 3, 3, bgPaint);
        canvas.DrawText(text, screenX, screenY, font, textPaint);
    }

    /// <summary>Point-editing handles for a Points-based shape (Line/Arrow/DoubleArrow/Polyline/Curve)
    /// — a square handle per anchor point, plus a smaller "insert here" ghost dot at each segment
    /// midpoint, replacing the 8 bounding-box handles for these kinds.</summary>
    private void DrawPointHandles(SKCanvas canvas, Shape shape, float scaleX, float scaleY)
    {
        var points = shape.Points!;

        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var strokePaint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = AccentColor,
        };
        using var ghostPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = AccentColor.WithAlpha(120) };

        for (var i = 0; i < points.Count - 1; i++)
        {
            var midX = (float)(shape.X + (points[i].X + points[i + 1].X) / 2) * scaleX;
            var midY = (float)(shape.Y + (points[i].Y + points[i + 1].Y) / 2) * scaleY;
            canvas.DrawCircle(midX, midY, HandleScreenSize / 2.5f, ghostPaint);
        }

        foreach (var p in points)
        {
            var x = (float)(shape.X + p.X) * scaleX;
            var y = (float)(shape.Y + p.Y) * scaleY;
            var rect = SKRect.Create(x - HandleScreenSize / 2, y - HandleScreenSize / 2, HandleScreenSize, HandleScreenSize);
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

        (ResizeHandle Handle, double X, double Y)[] points =
        [
            (ResizeHandle.TopLeft, left, top),
            (ResizeHandle.Top, midX, top),
            (ResizeHandle.TopRight, right, top),
            (ResizeHandle.Right, right, midY),
            (ResizeHandle.BottomRight, right, bottom),
            (ResizeHandle.Bottom, midX, bottom),
            (ResizeHandle.BottomLeft, left, bottom),
            (ResizeHandle.Left, left, midY),
        ];

        if (shape.Rotation == 0)
        {
            foreach (var p in points)
            {
                yield return p;
            }

            yield break;
        }

        var center = new Point(midX, midY);
        foreach (var (handle, x, y) in points)
        {
            var rotated = RotateAroundCenter(new Point(x, y), center, shape.Rotation);
            yield return (handle, rotated.X, rotated.Y);
        }
    }

    /// <summary>Rotates <paramref name="p"/> by <paramref name="degrees"/> clockwise around
    /// <paramref name="center"/>, matching the convention <see cref="SKMatrix.CreateRotationDegrees(float, float, float)"/>
    /// uses to rotate the rendered shape geometry — handle positions must use the identical
    /// formula so they stay visually attached to the (rotated) shape outline.</summary>
    private static Point RotateAroundCenter(Point p, Point center, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = p.X - center.X;
        var dy = p.Y - center.Y;
        return new Point(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
    }

    /// <summary>Position of the floating rotate handle, offset above the shape's (rotated) top-mid edge.</summary>
    private static (double X, double Y) ComputeRotateHandlePosition(Shape shape)
    {
        const double offset = 20;
        var centerX = shape.X + shape.Width / 2;
        var centerY = shape.Y + shape.Height / 2;
        var basePoint = new Point(centerX, shape.Y - offset);

        if (shape.Rotation == 0)
        {
            return (basePoint.X, basePoint.Y);
        }

        var rotated = RotateAroundCenter(basePoint, new Point(centerX, centerY), shape.Rotation);
        return (rotated.X, rotated.Y);
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

    private bool HitTestRotateHandle(Point docPoint, Shape shape)
    {
        var tolerance = HandleHitToleranceDip / Zoom;
        var (x, y) = ComputeRotateHandlePosition(shape);
        return Math.Abs(docPoint.X - x) <= tolerance && Math.Abs(docPoint.Y - y) <= tolerance;
    }

    /// <summary>Hit-tests a Points-based shape's point handles and segment-midpoint "insert here"
    /// ghosts. Returns the anchor/segment index and whether it was a midpoint hit.</summary>
    private (int Index, bool IsMidpoint)? HitTestPointHandle(Point docPoint, Shape shape)
    {
        var points = shape.Points;
        if (points is null)
        {
            return null;
        }

        var tolerance = HandleHitToleranceDip / Zoom;

        for (var i = 0; i < points.Count; i++)
        {
            var x = shape.X + points[i].X;
            var y = shape.Y + points[i].Y;
            if (Math.Abs(docPoint.X - x) <= tolerance && Math.Abs(docPoint.Y - y) <= tolerance)
            {
                return (i, false);
            }
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var midX = shape.X + (points[i].X + points[i + 1].X) / 2;
            var midY = shape.Y + (points[i].Y + points[i + 1].Y) / 2;
            if (Math.Abs(docPoint.X - midX) <= tolerance && Math.Abs(docPoint.Y - midY) <= tolerance)
            {
                return (i, true);
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

        if (_context.ActiveTool == ActiveTool.Shape && _context.SelectedShapeKind == ShapeKind.Text)
        {
            BeginTextEdit(point, null);
            return;
        }

        if (_context.ActiveTool == ActiveTool.Shape)
        {
            BeginPlacing(point);
            return;
        }

        if (_context.ActiveTool == ActiveTool.PathDraw)
        {
            HandlePathDrawClick(point, e.ClickCount);
            return;
        }

        if (_context.ActiveTool == ActiveTool.PaintBucket)
        {
            HandlePaintBucketClick(point);
            return;
        }

        // Select tool.
        if (e.ClickCount == 2)
        {
            var dblHit = HitTestShape(point);
            if (dblHit is { } hitValue && hitValue.Shape.Kind == ShapeKind.Text && !IsLocked(hitValue.Shape))
            {
                BeginTextEdit(new Point(hitValue.Shape.X, hitValue.Shape.Y), hitValue.Shape);
                return;
            }
        }

        var ctrlHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var soleSelectionEditable = _context.SelectedShapes.Count == 1 && !IsLocked(_context.SelectedShapes[0]);

        if (soleSelectionEditable && _context.SelectedShapes[0].Points is { Count: > 0 })
        {
            var pointShape = _context.SelectedShapes[0];
            var pointHit = HitTestPointHandle(point, pointShape);

            if (pointHit is { IsMidpoint: true })
            {
                InsertPointAt(pointShape, pointHit.Value.Index, point);
                return;
            }

            if (pointHit is { IsMidpoint: false })
            {
                if (ctrlHeld && pointShape.Points!.Count > 2)
                {
                    RemovePointAt(pointShape, pointHit.Value.Index);
                }
                else
                {
                    BeginDragPoint(pointShape, pointHit.Value.Index);
                }

                return;
            }
        }
        else if (soleSelectionEditable && _context.SelectedShapes[0].Kind == ShapeKind.Box3D &&
                 HitTestDepthHandle(point, _context.SelectedShapes[0]))
        {
            BeginDepthDrag(_context.SelectedShapes[0]);
            return;
        }
        else if (soleSelectionEditable && HitTestRotateHandle(point, _context.SelectedShapes[0]))
        {
            BeginRotate(_context.SelectedShapes[0]);
            return;
        }
        else if (soleSelectionEditable && HitTestHandle(point, _context.SelectedShapes[0]) is { } handle)
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
                // Clicking any one shape in a group selects every shape sharing its GroupId, so
                // the group acts as one unit.
                if (shape.GroupId is { } groupId)
                {
                    _context.SelectedShapes.Clear();
                    foreach (var groupMate in _context.Document.Layers.SelectMany(l => l.Shapes).Where(s => s.GroupId == groupId))
                    {
                        _context.SelectedShapes.Add(groupMate);
                    }
                }
                else
                {
                    _context.SelectedShape = shape;
                }
            }

            _context.SelectedLayer = layer;

            if (!IsLocked(shape))
            {
                _isMovingSelection = true;
                _moveLastPoint = point;
                _moveUndoBefore = _context.SelectedShapes.Select(ShapeSnapshot.Capture).ToList();
                CaptureMouse();
            }

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

    /// <summary>True if the shape itself is locked, or it sits in a locked layer — either way it
    /// can be selected but not moved/resized/rotated/deleted.</summary>
    private bool IsLocked(Shape shape)
    {
        if (shape.Locked)
        {
            return true;
        }

        var layer = _context?.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
        return layer?.Locked ?? false;
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

        if (TwoPointLineKinds.Contains(_context.SelectedShapeKind))
        {
            _pendingShape = new Shape
            {
                Kind = _context.SelectedShapeKind,
                X = point.X,
                Y = point.Y,
                Width = 1,
                Height = 1,
                FillColor = _context.CurrentFillColor,
                Points = [new CorePoint(0, 0), new CorePoint(0, 0)],
            };
        }
        else
        {
            _pendingShape = new Shape
            {
                Kind = _context.SelectedShapeKind,
                X = point.X,
                Y = point.Y,
                Width = 0,
                Height = 0,
                FillColor = _context.CurrentFillColor,
                CornerRadius = _context.SelectedShapeKind == ShapeKind.RoundedRectangle ? 12 : 0,
            };
        }

        CaptureMouse();
    }

    private void BeginResize(ResizeHandle handle, Shape shape, Point point)
    {
        _isResizing = true;
        _resizeHandle = handle;
        _resizeShape = shape;
        _resizeStartRect = (shape.X, shape.Y, shape.Width, shape.Height);
        _resizeStartPoint = point;
        _resizeCenter = new Point(shape.X + shape.Width / 2, shape.Y + shape.Height / 2);
        _resizeRotation = shape.Rotation;
        _resizeUndoBefore = ShapeSnapshot.Capture(shape);
        CaptureMouse();
    }

    private void BeginRotate(Shape shape)
    {
        _isRotating = true;
        _rotateShape = shape;
        _rotateUndoBefore = ShapeSnapshot.Capture(shape);
        CaptureMouse();
    }

    private void BeginDepthDrag(Shape shape)
    {
        _isDraggingDepth = true;
        _depthDragShape = shape;
        _depthDragUndoBefore = ShapeSnapshot.Capture(shape);
        CaptureMouse();
    }

    /// <summary>Click-to-add-point placement for Polyline/Curve. First click starts the shape
    /// with a confirmed anchor plus a trailing "ghost" point that <see cref="OnMouseMove"/> keeps
    /// tracking the cursor; each subsequent single click confirms the ghost and starts a new one;
    /// a double-click (ClickCount 2) finishes via <see cref="FinishPathDraw"/>.</summary>
    private void HandlePathDrawClick(Point point, int clickCount)
    {
        if (_context is null)
        {
            return;
        }

        if (!_isPathDrawing || _pendingPathShape is null)
        {
            _isPathDrawing = true;
            _pendingPathShape = new Shape
            {
                Kind = _context.SelectedShapeKind,
                X = point.X,
                Y = point.Y,
                Width = 1,
                Height = 1,
                FillColor = _context.CurrentFillColor,
                Dashed = _context.SelectedShapeKind == ShapeKind.Polyline,
                Points = [new CorePoint(0, 0), new CorePoint(0, 0)],
            };
            _context.RequestRedraw();
            return;
        }

        if (clickCount >= 2)
        {
            FinishPathDraw();
            return;
        }

        var list = _pendingPathShape.Points!.ToList();
        list.Add(list[^1]);
        _pendingPathShape.Points = list;
        _context.RequestRedraw();
    }

    private void InsertPointAt(Shape shape, int afterIndex, Point docPoint)
    {
        if (_context is null)
        {
            return;
        }

        var before = ShapeSnapshot.Capture(shape);
        var list = shape.Points!.ToList();
        list.Insert(afterIndex + 1, new CorePoint(docPoint.X - shape.X, docPoint.Y - shape.Y));
        shape.Points = list;
        PathBoundsCalculator.Recompute(shape);

        var after = ShapeSnapshot.Capture(shape);
        _context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [after]));
        _context.RequestRedraw();
    }

    private void RemovePointAt(Shape shape, int index)
    {
        if (_context is null)
        {
            return;
        }

        var before = ShapeSnapshot.Capture(shape);
        var list = shape.Points!.ToList();
        list.RemoveAt(index);
        shape.Points = list;
        PathBoundsCalculator.Recompute(shape);

        var after = ShapeSnapshot.Capture(shape);
        _context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [after]));
        _context.RequestRedraw();
    }

    private void BeginDragPoint(Shape shape, int index)
    {
        _isDraggingPoint = true;
        _pointEditShape = shape;
        _draggedPointIndex = index;
        _pointDragUndoBefore = ShapeSnapshot.Capture(shape);
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

    /// <summary>Opens an inline WPF TextBox overlay, positioned over the document point, for
    /// typing a Text shape's content. <paramref name="existingShape"/> is null when creating a
    /// brand-new Text shape (click with the Text tool active), or the shape being re-edited
    /// (double-click an existing Text shape with the Select tool).</summary>
    private void BeginTextEdit(Point docOrigin, Shape? existingShape)
    {
        if (_context is null)
        {
            return;
        }

        CancelTextEdit();

        _textEditShape = existingShape;
        _textEditOrigin = docOrigin;
        _textEditLayer = existingShape is not null
            ? _context.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(existingShape))
            : _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();

        var fontSize = existingShape?.TextFontSize ?? 24;

        var textBox = new TextBox
        {
            Text = existingShape?.TextContent ?? string.Empty,
            FontSize = fontSize * Zoom,
            MinWidth = 40 * Zoom,
            Padding = new Thickness(2),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xB1, 0xFF)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = false,
        };

        var screenPoint = SkiaCanvas.TranslatePoint(new Point(docOrigin.X * Zoom, docOrigin.Y * Zoom), TextEditOverlay);
        Canvas.SetLeft(textBox, screenPoint.X);
        Canvas.SetTop(textBox, screenPoint.Y);

        TextEditOverlay.Children.Add(textBox);
        _textEditBox = textBox;

        textBox.KeyDown += TextEditBox_OnKeyDown;
        textBox.LostFocus += TextEditBox_OnLostFocus;

        textBox.Focus();
        Keyboard.Focus(textBox);
        textBox.SelectAll();
    }

    private void TextEditBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTextEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTextEdit();
            _context?.RequestRedraw();
            e.Handled = true;
        }
    }

    private void TextEditBox_OnLostFocus(object sender, RoutedEventArgs e) => CommitTextEdit();

    /// <summary>Reads the typed text back out of the overlay TextBox and either creates a new
    /// Text shape, updates the one being re-edited, or (if left blank) removes it — then closes
    /// the overlay. No-op if no edit is in progress (also reached harmlessly via LostFocus after
    /// <see cref="CancelTextEdit"/> already tore the box down).</summary>
    private void CommitTextEdit()
    {
        if (_textEditBox is null || _context is null)
        {
            return;
        }

        var text = _textEditBox.Text;
        var layer = _textEditLayer;
        var existingShape = _textEditShape;
        var origin = _textEditOrigin;

        RemoveTextEditBox();

        if (string.IsNullOrWhiteSpace(text))
        {
            if (existingShape is not null && layer is not null)
            {
                var index = layer.Shapes.IndexOf(existingShape);
                if (index >= 0)
                {
                    layer.Shapes.RemoveAt(index);
                    _context.UndoManager.Push(UndoableAction.ForRemove([(layer.Shapes, existingShape, index)]));
                    _context.SelectedShapes.Remove(existingShape);
                }
            }

            _context.RequestRedraw();
            return;
        }

        if (layer is null)
        {
            return;
        }

        var bold = existingShape?.TextBold ?? false;
        var italic = existingShape?.TextItalic ?? false;
        var fontSize = existingShape?.TextFontSize ?? 24;

        using var typeface = SKTypeface.FromFamilyName("Segoe UI",
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, (float)fontSize);
        using var measurePaint = new SKPaint();
        font.MeasureText(text, out var bounds, measurePaint);
        var width = Math.Max(20, bounds.Width + 8);
        var height = Math.Max(20, bounds.Height + 8);

        if (existingShape is not null)
        {
            var before = ShapeSnapshot.Capture(existingShape);
            existingShape.TextContent = text;
            existingShape.Width = width;
            existingShape.Height = height;
            var after = ShapeSnapshot.Capture(existingShape);
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([existingShape], [before], [after]));
        }
        else
        {
            var shape = new Shape
            {
                Kind = ShapeKind.Text,
                X = origin.X,
                Y = origin.Y,
                Width = width,
                Height = height,
                FillColor = _context.CurrentFillColor,
                TextContent = text,
            };

            layer.Shapes.Insert(0, shape);
            _context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
            _context.SelectedShape = shape;
            _context.SelectedLayer = layer;
        }

        _context.RequestRedraw();
    }

    /// <summary>Discards the in-progress edit (Escape, or starting a new gesture elsewhere)
    /// without touching the shape being edited.</summary>
    private void CancelTextEdit() => RemoveTextEditBox();

    private void RemoveTextEditBox()
    {
        if (_textEditBox is null)
        {
            return;
        }

        var box = _textEditBox;
        _textEditBox = null;
        _textEditShape = null;
        _textEditLayer = null;

        box.KeyDown -= TextEditBox_OnKeyDown;
        box.LostFocus -= TextEditBox_OnLostFocus;
        TextEditOverlay.Children.Remove(box);
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

            if (_pendingShape.Points is not null)
            {
                // Re-anchor to the fixed drag-start point every frame (not the previous frame's
                // possibly-recomputed X/Y) so Recompute's absolute-position math never drifts.
                _pendingShape.X = _placeStart.X;
                _pendingShape.Y = _placeStart.Y;
                _pendingShape.Points = [new CorePoint(0, 0), new CorePoint(point.X - _placeStart.X, point.Y - _placeStart.Y)];
                PathBoundsCalculator.Recompute(_pendingShape);
            }
            else
            {
                _pendingShape.X = SnapIfEnabled(Math.Min(_placeStart.X, point.X));
                _pendingShape.Y = SnapIfEnabled(Math.Min(_placeStart.Y, point.Y));
                _pendingShape.Width = SnapIfEnabled(Math.Abs(point.X - _placeStart.X));
                _pendingShape.Height = SnapIfEnabled(Math.Abs(point.Y - _placeStart.Y));
            }

            _context?.RequestRedraw();
            return;
        }

        if (_isPathDrawing && _pendingPathShape is not null)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            point = new Point(SnapIfEnabled(point.X), SnapIfEnabled(point.Y));
            var list = _pendingPathShape.Points!.ToList();
            list[^1] = new CorePoint(point.X - _pendingPathShape.X, point.Y - _pendingPathShape.Y);
            _pendingPathShape.Points = list;
            PathBoundsCalculator.Recompute(_pendingPathShape);
            _context?.RequestRedraw();
            return;
        }

        if (_isDraggingPoint && _pointEditShape is not null && _draggedPointIndex is { } draggedIndex)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            point = new Point(SnapIfEnabled(point.X), SnapIfEnabled(point.Y));
            var list = _pointEditShape.Points!.ToList();
            list[draggedIndex] = new CorePoint(point.X - _pointEditShape.X, point.Y - _pointEditShape.Y);
            _pointEditShape.Points = list;
            PathBoundsCalculator.Recompute(_pointEditShape);
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

        if (_isRotating && _rotateShape is not null)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            var center = new Point(_rotateShape.X + _rotateShape.Width / 2, _rotateShape.Y + _rotateShape.Height / 2);
            var angleDeg = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI;
            _rotateShape.Rotation = angleDeg + 90;
            _context?.RequestRedraw();
            return;
        }

        if (_isDraggingDepth && _depthDragShape is not null)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            var shape = _depthDragShape;
            var maxDepth = Math.Min(shape.Width, shape.Height) * 0.6;
            var rawDepth = ((point.X - shape.X) + (point.Y - shape.Y)) / 2.0;
            shape.Depth3D = Math.Clamp(rawDepth, 0, maxDepth);
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

            // Snapping a multi-shape drag would distort relative offsets between shapes, so only
            // snap when moving a single shape.
            if (_context.SelectedShapes.Count == 1)
            {
                var shape = _context.SelectedShapes[0];
                shape.X = SnapIfEnabled(shape.X);
                shape.Y = SnapIfEnabled(shape.Y);
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

        // Rotate both points into the shape's local (unrotated) frame, using the rotation/center
        // frozen at gesture-start, so the left/right/top/bottom math below stays axis-aligned
        // regardless of the shape's on-screen rotation.
        var localPoint = point;
        var localStartPoint = _resizeStartPoint;
        if (_resizeRotation != 0)
        {
            localPoint = RotateAroundCenter(point, _resizeCenter, -_resizeRotation);
            localStartPoint = RotateAroundCenter(_resizeStartPoint, _resizeCenter, -_resizeRotation);
        }

        var dx = localPoint.X - localStartPoint.X;
        var dy = localPoint.Y - localStartPoint.Y;
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

        _resizeShape.X = SnapIfEnabled(newX);
        _resizeShape.Y = SnapIfEnabled(newY);
        _resizeShape.Width = SnapIfEnabled(newW);
        _resizeShape.Height = SnapIfEnabled(newH);
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
            ReleaseMouseCapture();

            if (_resizeShape is not null && _context is not null)
            {
                var after = ShapeSnapshot.Capture(_resizeShape);
                if (!after.Equals(_resizeUndoBefore))
                {
                    _context.UndoManager.Push(
                        UndoableAction.ForPropertyEdit([_resizeShape], [_resizeUndoBefore], [after]));
                }
            }

            _resizeShape = null;
            return;
        }

        if (_isRotating)
        {
            _isRotating = false;
            ReleaseMouseCapture();

            if (_rotateShape is not null && _context is not null)
            {
                var after = ShapeSnapshot.Capture(_rotateShape);
                if (!after.Equals(_rotateUndoBefore))
                {
                    _context.UndoManager.Push(
                        UndoableAction.ForPropertyEdit([_rotateShape], [_rotateUndoBefore], [after]));
                }
            }

            _rotateShape = null;
            return;
        }

        if (_isDraggingDepth)
        {
            _isDraggingDepth = false;
            ReleaseMouseCapture();

            if (_depthDragShape is not null && _context is not null)
            {
                var after = ShapeSnapshot.Capture(_depthDragShape);
                if (!after.Equals(_depthDragUndoBefore))
                {
                    _context.UndoManager.Push(
                        UndoableAction.ForPropertyEdit([_depthDragShape], [_depthDragUndoBefore], [after]));
                }
            }

            _depthDragShape = null;
            return;
        }

        if (_isDraggingPoint)
        {
            _isDraggingPoint = false;
            ReleaseMouseCapture();

            if (_pointEditShape is not null && _context is not null)
            {
                var after = ShapeSnapshot.Capture(_pointEditShape);
                if (!after.Equals(_pointDragUndoBefore))
                {
                    _context.UndoManager.Push(
                        UndoableAction.ForPropertyEdit([_pointEditShape], [_pointDragUndoBefore], [after]));
                }
            }

            _pointEditShape = null;
            _draggedPointIndex = null;
            return;
        }

        if (_isMovingSelection)
        {
            _isMovingSelection = false;
            ReleaseMouseCapture();

            if (_moveUndoBefore is not null && _context is not null && _moveUndoBefore.Count == _context.SelectedShapes.Count)
            {
                var shapes = _context.SelectedShapes.ToList();
                var after = shapes.Select(ShapeSnapshot.Capture).ToList();
                _context.UndoManager.Push(UndoableAction.ForPropertyEdit(shapes, _moveUndoBefore, after));
            }

            _moveUndoBefore = null;
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

        var shape = _pendingShape;
        var isValidBox = shape is { Width: >= 2, Height: >= 2 };
        // A 2-point line only needs ONE dimension to span >=2 (a perfectly horizontal/vertical
        // line has the other dimension clamped to a minimum of 1 by PathBoundsCalculator).
        var isValidLine = shape?.Points is { Count: >= 2 } && (shape.Width >= 2 || shape.Height >= 2);

        if (shape is not null && (isValidBox || isValidLine) && _context is not null)
        {
            var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
            if (layer is not null)
            {
                layer.Shapes.Insert(0, shape);
                _context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
                _context.SelectedLayer = layer;
                _context.SelectedShape = shape;
            }
        }

        _pendingShape = null;
        _context?.RequestRedraw();
    }
}
