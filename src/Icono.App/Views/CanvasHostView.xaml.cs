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
    private static readonly SKColor AccentColor = new(0x5A, 0xB0, 0xFF);

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
    private Point _moveStartPoint;
    private List<(double X, double Y)> _moveStartPositions = [];
    private List<ShapeSnapshot>? _moveUndoBefore;

    private bool _isResizing;
    private ResizeHandle _resizeHandle;
    private Shape? _resizeShape;
    private (double X, double Y, double W, double H) _resizeStartRect;
    private (double X, double Y, double W, double H) _resizeFrameStart;
    private Point _resizeStartPoint;
    private Point _resizeCenter;
    private double _resizeRotation;
    private ShapeSnapshot _resizeUndoBefore;

    private bool _isGroupResizing;
    private ResizeHandle _groupResizeHandle;
    private List<Shape> _groupResizeShapes = [];
    private List<(double X, double Y, double W, double H)> _groupResizeStartRects = [];
    private List<ShapeSnapshot> _groupResizeUndoBefore = [];
    private (double X, double Y, double W, double H) _groupResizeStartBounds;
    private Point _groupResizeStartPoint;

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
                _context.PropertyChanged -= OnContextPropertyChanged;
                _context.SelectedShapes.CollectionChanged -= OnSelectionChanged;
            }

            _context = value;

            if (_context is not null)
            {
                _context.Invalidated += OnContextInvalidated;
                _context.PropertyChanged += OnContextPropertyChanged;
                _context.SelectedShapes.CollectionChanged += OnSelectionChanged;
            }

            UpdateCanvasFrameSize();
            SkiaCanvas.InvalidateVisual();
        }
    }

    public CanvasHostView()
    {
        InitializeComponent();

        // Clicking the canvas takes keyboard focus away from whatever panel field had it, so keys such as
        // Delete ("Entf") act on the selected drawing and a half-edited value field commits first.
        Focusable = true;
        PreviewMouseDown += (_, e) =>
        {
            // A click outside the text editor finishes the typing first (so it is kept), then the click does its job.
            CommitTextEditUnlessInside(e.OriginalSource);

            if (TextEditOverlay.Children.Count == 0 && !IsInsideQuickPopup(e.OriginalSource))
            {
                Focus();
            }
        };

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && !IsInsideQuickPopup(e.OriginalSource))
            {
                ShowLineQuickPopup();
            }
        };
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
        // A ContextMenu opened via IsOpen = true (instead of assigning FrameworkElement.ContextMenu)
        // isn't inserted into this control's logical tree, so it can silently fall back to the OS's
        // light default chrome instead of the app's dark theme — force our themed Style explicitly.
        if (TryFindResource(typeof(ContextMenu)) is Style contextMenuStyle)
        {
            menu.Style = contextMenuStyle;
        }

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

        var allUnlocked = selected.Count > 0 && selected.All(s => !IsLocked(s));
        AddItem("Union", _context.UnionSelection, selected.Count >= 2 && allUnlocked);
        AddItem("Trim (first selected = cutter)", _context.TrimSelection, selected.Count == 2 && allUnlocked);
        AddItem("Merge to One Shape", _context.MergeSelection, selected.Count >= 2 && allUnlocked);
        AddItem("Reflect", _context.ReflectSelection, selected.Count >= 1 && allUnlocked);
        AddItem("Offset...", ShowOffsetDialog, selected.Count >= 1 && allUnlocked);
        AddItem("Match Properties (copy this look)", _context.BeginMatch, selected.Count == 1);

        if (selected.Count == 1 && !IsLocked(selected[0]) && selected[0].Points is null &&
            ShapeGeometryFactory.SupportsPointEditing(selected[0].Kind))
        {
            var shape = selected[0];
            AddItem("Edit Points", () => _context.ConvertToEditablePoints(shape));
        }

        if (selected.Count == 1 && !IsLocked(selected[0]) && selected[0].Kind == ShapeKind.Text)
        {
            var shape = selected[0];
            AddItem("Convert to Shape", () => _context.ConvertTextToShape(shape));
        }

        menu.Items.Add(new Separator());

        // Disabled when the selection is already exactly one existing group (clicking any grouped
        // shape auto-selects every shape sharing its GroupId — see OnMouseDown) — re-grouping an
        // already-fully-grouped selection is a no-op, so offering it as if it did something is
        // just confusing.
        var alreadyOneGroup = selected.Count >= 2 &&
            selected.All(s => s.GroupId is { } gid && gid == selected[0].GroupId);
        AddItem("Group", _context.GroupSelection, selected.Count >= 2 && !alreadyOneGroup);
        AddItem("Ungroup", _context.UngroupSelection, selected.Any(s => s.GroupId is not null));

        var allLocked = selected.Count > 0 && selected.All(s => s.Locked);
        AddItem(allLocked ? "Unlock" : "Lock", () => _context.SetSelectionLocked(!allLocked), selected.Count > 0);

        menu.Items.Add(new Separator());
        AddItem("Bring to Front", _context.BringSelectionToFront, selected.Count > 0);
        AddItem("Send to Back", _context.SendSelectionToBack, selected.Count > 0);

        menu.IsOpen = true;
    }

    private void ShowOffsetDialog()
    {
        if (_context is null)
        {
            return;
        }

        var dialog = new OffsetDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            _context.OffsetSelection(dialog.Distance);
        }
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
    public void FinishPathDraw(bool closeWall = false)
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

        if (points.Count < 2 || (shape.Kind == ShapeKind.Dimension && points.Count < DimensionPointsNeeded(shape.DimKind)))
        {
            _context.RequestRedraw();
            return;
        }

        shape.Points = points;
        // A wall that comes back to its start point is a closed room.
        if (closeWall && shape.Kind == ShapeKind.Wall && points.Count >= 3)
        {
            shape.WallClosed = true;
        }

        _wallClosePreview = false;
        PathBoundsCalculator.Recompute(shape);

        var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
        if (layer is not null)
        {
            layer.Shapes.Insert(0, shape);
            _context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
            _context.SelectedLayer = layer;
            _context.SelectedShape = shape;
        }

        ConnectionRouter.AttachEnds(_context.Document, shape);
        if (shape.Kind == ShapeKind.Dimension)
        {
            // A dimension whose points sit on a shape sticks to it: it follows when the shape moves or is resized.
            DimensionRouter.AttachPoints(_context.Document, shape);
        }

        ClearAnchorProbe();
        _context.RequestRedraw();
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (CanvasHostView)d;
        host.HideLineQuickPopup();
        host.UpdateCanvasFrameSize();
    }

    // ----- Quick line popup (form + both line ends, shown next to a clicked line/arrow) ---------------

    private void OnSelectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        HideLineQuickPopup();

    private void HideLineQuickPopup() => LineQuickPopupControl.Visibility = Visibility.Collapsed;

    // ----- Dimensions ---------------------------------------------------------------------------------------

    /// <summary>How many clicks a dimension of this kind needs: its points, the last one fixing where the dimension line runs.</summary>
    private static int DimensionPointsNeeded(DimensionKind kind) => kind switch
    {
        DimensionKind.Angular => 4,
        DimensionKind.Radius or DimensionKind.Diameter or DimensionKind.Length => 2,
        _ => 3,
    };

    // ----- Match: carry the look of one object over to others ----------------------------------------------------

    /// <summary>The first click (or Ctrl+click) picks the source; every further click paints its look onto the object under the pointer.</summary>
    private void HandleMatchClick(Point point)
    {
        if (_context is null || HitTestShape(point) is not { } hit)
        {
            return;
        }

        if (_context.MatchSource is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _context.MatchSource = hit.Shape;
            _context.Notify("Match: source picked - click the objects that should get its look. Esc ends.");
            return;
        }

        _context.MatchTo(hit.Shape);
    }

    private void OnContextPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorContext.ActiveTool))
        {
            Cursor = _context?.ActiveTool == ActiveTool.Match ? Cursors.Cross : null;
        }
    }

    private void DrawMatchSource(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_context is { ActiveTool: ActiveTool.Match, MatchSource: { } source })
        {
            DrawSelectionOutline(canvas, source, scaleX, scaleY);
        }
    }

    // ----- Measuring anywhere: snapping, the ruler and the gap between two objects ----------------------------

    private static readonly SKColor MeasureOrange = new(0xF3, 0x9C, 0x12);

    private MeasureSnap? _measureSnap;

    private Point? _rulerStart;
    private Point _rulerEnd;
    private bool _rulerFrozen;

    private Shape? _gapFirst;
    private Shape? _gapSecond;
    private Shape? _gapPreview;

    /// <summary>Corner, middle, centre or edge of any shape or wall near the pointer (the point snaps there); remembered for the marker.</summary>
    private Point? SnapToMeasurable(Point point)
    {
        if (_context is null || WallSnappingSuspended)
        {
            _measureSnap = null;
            return null;
        }

        _measureSnap = MeasureSnapper.Find(_context.Document, new SKPoint((float)point.X, (float)point.Y), AnchorSnapDip / Zoom);
        return _measureSnap is null ? null : new Point(_measureSnap.Point.X, _measureSnap.Point.Y);
    }

    /// <summary>Cancels a ruler measurement or a half-made gap dimension. True when there was something to cancel.</summary>
    public bool CancelMeasure()
    {
        var had = _rulerStart is not null || _gapFirst is not null;
        _rulerStart = null;
        _rulerFrozen = false;
        ResetGap();
        _measureSnap = null;
        if (had)
        {
            SkiaCanvas.InvalidateVisual();
        }

        return had;
    }

    private void ResetGap()
    {
        _gapFirst = null;
        _gapSecond = null;
        _gapPreview = null;
    }

    private bool IsMeasureTool => _context?.ActiveTool is ActiveTool.Measure or ActiveTool.MeasureGap;

    /// <summary>The ruler: first click sets the start, the second ends the measurement (it stays on screen until the next click or Esc).
    /// Nothing is added to the drawing.</summary>
    private void HandleRulerClick(Point point)
    {
        var snapped = SnapToMeasurable(point) ?? point;
        if (_rulerStart is null || _rulerFrozen)
        {
            _rulerStart = snapped;
            _rulerEnd = snapped;
            _rulerFrozen = false;
        }
        else
        {
            _rulerEnd = RulerEnd(point);
            _rulerFrozen = true;
        }

        SkiaCanvas.InvalidateVisual();
    }

    /// <summary>The measuring end: snapped to the drawing, or (Shift) locked horizontal / vertical from the start.</summary>
    private Point RulerEnd(Point raw)
    {
        var snapped = SnapToMeasurable(raw) ?? raw;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _rulerStart is { } start)
        {
            return Math.Abs(snapped.X - start.X) >= Math.Abs(snapped.Y - start.Y)
                ? new Point(snapped.X, start.Y)
                : new Point(start.X, snapped.Y);
        }

        return snapped;
    }

    private void UpdateRulerHover(Point raw)
    {
        if (_rulerStart is not null && !_rulerFrozen)
        {
            _rulerEnd = RulerEnd(raw);
        }
        else
        {
            SnapToMeasurable(raw);
        }

        SkiaCanvas.InvalidateVisual();
    }

    /// <summary>"Distance between objects": the first click picks an object, the second the object to measure to. The dimension joins
    /// their nearest points and stays attached to both.</summary>
    private void HandleGapClick(Point point)
    {
        if (_context is null)
        {
            return;
        }

        var hit = HitTestMeasurable(point, exclude: null);
        if (hit is null)
        {
            return;
        }

        if (_gapFirst is null)
        {
            _gapFirst = hit;
            _gapSecond = null;
            SkiaCanvas.InvalidateVisual();
            return;
        }

        if (ReferenceEquals(hit, _gapFirst))
        {
            return;
        }

        var dim = DimensionBuilder.CreateGap(_gapFirst, hit, _context.DimensionUnit);
        _gapFirst = null;
        _gapSecond = null;
        if (dim is null)
        {
            return;
        }

        var layer = _context.SelectedLayer ?? _context.Document.Layers.FirstOrDefault();
        if (layer is null)
        {
            return;
        }

        layer.Shapes.Insert(0, dim);
        _context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, dim, 0));
        _context.RequestRedraw();
        _context.Notify("Distance dimension added. It follows both objects.");
    }

    /// <summary>The object under the pointer that can be measured (walls are hit on their band, everything else on its frame or outline).</summary>
    private Shape? HitTestMeasurable(Point point, Shape? exclude)
    {
        if (_context is null)
        {
            return null;
        }

        var tolerance = 6 / Zoom;
        foreach (var layer in _context.Document.Layers.Where(l => l.Visible))
        {
            foreach (var shape in layer.Shapes)
            {
                if (ReferenceEquals(shape, exclude) || !OutlineGeometry.IsMeasurable(shape))
                {
                    continue;
                }

                if (shape.Kind == ShapeKind.Wall)
                {
                    var spot = WallEditor.Nearest(shape, point.X, point.Y, 0);
                    if (spot is { } onWall && onWall.Distance <= shape.WallThickness / 2 + tolerance)
                    {
                        return shape;
                    }

                    continue;
                }

                if (OutlineGeometry.HitTest(shape, point.X, point.Y, tolerance))
                {
                    return shape;
                }
            }
        }

        return null;
    }

    private void UpdateGapHover(Point raw)
    {
        var hovered = HitTestMeasurable(raw, _gapFirst);
        if (!ReferenceEquals(hovered, _gapSecond))
        {
            _gapSecond = hovered;
            _gapPreview = hovered is null || _context is null || _gapFirst is null
                ? null
                : DimensionBuilder.CreateGap(_gapFirst, hovered, _context.DimensionUnit);
        }

        SkiaCanvas.InvalidateVisual();
    }

    private void DrawMeasure(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_context is null)
        {
            return;
        }

        var showSnap = _measureSnap is not null &&
            (IsMeasureTool || (_isPathDrawing && _pendingPathShape is { Kind: ShapeKind.Dimension }) ||
             (_context.ActiveTool == ActiveTool.PathDraw && _context.SelectedShapeKind == ShapeKind.Dimension));
        if (showSnap)
        {
            var snap = _measureSnap!;
            using var mark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = MeasureOrange };
            var x = snap.Point.X * scaleX;
            var y = snap.Point.Y * scaleY;
            switch (snap.Kind)
            {
                case MeasureSnapKind.Corner:
                    canvas.DrawRect(x - 6, y - 6, 12, 12, mark);
                    break;
                case MeasureSnapKind.Midpoint:
                {
                    using var triangle = new SKPath();
                    triangle.MoveTo(x, y - 7);
                    triangle.LineTo(x + 7, y + 6);
                    triangle.LineTo(x - 7, y + 6);
                    triangle.Close();
                    canvas.DrawPath(triangle, mark);
                    break;
                }

                case MeasureSnapKind.Center:
                    canvas.DrawCircle(x, y, 6, mark);
                    canvas.DrawCircle(x, y, 1.5f, mark);
                    break;
                case MeasureSnapKind.Edge:
                    canvas.DrawLine(x - 5, y - 5, x + 5, y + 5, mark);
                    canvas.DrawLine(x - 5, y + 5, x + 5, y - 5, mark);
                    break;
                default:
                    canvas.DrawCircle(x, y, 5, mark);
                    break;
            }

            var name = snap.Kind switch
            {
                MeasureSnapKind.Corner => "corner",
                MeasureSnapKind.Midpoint => "midpoint",
                MeasureSnapKind.Center => "centre",
                MeasureSnapKind.Edge => "on edge",
                _ => "point",
            };
            DrawPill(canvas, name, x + 14, y - 30);
        }

        if (_context.ActiveTool == ActiveTool.Measure && _rulerStart is { } start)
        {
            var end = _rulerEnd;
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var angle = Geo.AngleDegrees(new SKPoint((float)start.X, (float)start.Y), new SKPoint((float)end.X, (float)end.Y));

            using var line = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, Color = MeasureOrange,
                PathEffect = _rulerFrozen ? null : SKPathEffect.CreateDash([6, 4], 0),
            };
            var sx = (float)start.X * scaleX;
            var sy = (float)start.Y * scaleY;
            var ex = (float)end.X * scaleX;
            var ey = (float)end.Y * scaleY;
            canvas.DrawLine(sx, sy, ex, ey, line);

            using var ends = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = MeasureOrange };
            canvas.DrawCircle(sx, sy, 4, ends);
            canvas.DrawCircle(ex, ey, 4, ends);

            // the horizontal and vertical parts, so each distance can be read
            using var parts = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0xF3, 0x9C, 0x12, 140),
                PathEffect = SKPathEffect.CreateDash([3, 4], 0),
            };
            canvas.DrawLine(sx, sy, ex, sy, parts);
            canvas.DrawLine(ex, sy, ex, ey, parts);

            var label = $"{DimensionRenderer.FormatAuto(length)}   ΔX {DimensionRenderer.FormatAuto(Math.Abs(dx))}   ΔY {DimensionRenderer.FormatAuto(Math.Abs(dy))}   {angle:0.#}°";
            DrawPill(canvas, label, (sx + ex) / 2 + 12, (sy + ey) / 2 - 34);
        }

        if (_context.ActiveTool == ActiveTool.MeasureGap && _gapFirst is { } first)
        {
            // the first object is outlined; with the pointer over a second one the gap is previewed
            using var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, Color = new SKColor(0xF3, 0x9C, 0x12, 200) };
            DrawOutlineOf(canvas, first, scaleX, scaleY, outline);

            if (_gapSecond is { } second && _gapPreview is { } preview)
            {
                canvas.Save();
                canvas.Scale(scaleX, scaleY); // the preview is in document units
                DimensionRenderer.Draw(canvas, preview, 255);
                canvas.Restore();
                DrawOutlineOf(canvas, second, scaleX, scaleY, outline);
            }
        }
    }

    private static void DrawOutlineOf(SKCanvas canvas, Shape shape, float scaleX, float scaleY, SKPaint paint)
    {
        using var path = ShapeGeometryFactory.Build(shape);
        path.Transform(SKMatrix.CreateScale(scaleX, scaleY));
        canvas.DrawPath(path, paint);
    }

    private static void DrawPill(SKCanvas canvas, string text, float x, float y)
    {
        using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        using var font = new SKFont(FontCache.Get("Segoe UI"), 12);
        var width = font.MeasureText(text);
        var rect = new SKRect(x, y, x + width + 14, y + 22);
        using var back = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0x1B, 0x23, 0x32, 225) };
        canvas.DrawRoundRect(rect, 7, 7, back);
        canvas.DrawText(text, rect.Left + 7, rect.Bottom - 6, font, textPaint);
    }

    // ----- Walls (drawing, closing, cutting) ----------------------------------------------------------------

    private bool _wallClosePreview;
    private WallEditor.Spot? _cutHover;

    private (double X, double Y) WallStart(Shape wall) => (wall.X + wall.Points![0].X, wall.Y + wall.Points[0].Y);

    /// <summary>True when the pointer is close enough to the first point of the wall being drawn to close it (three corners at least).</summary>
    private bool IsNearWallStart(Point point)
    {
        if (_pendingPathShape is not { Kind: ShapeKind.Wall } wall || wall.Points is null || wall.Points.Count - 1 < 3)
        {
            return false;
        }

        var (sx, sy) = WallStart(wall);
        return Math.Sqrt((point.X - sx) * (point.X - sx) + (point.Y - sy) * (point.Y - sy)) <= AnchorSnapDip * 1.4 / Zoom;
    }

    private WallSnapResult? _wallSnap;

    private bool WallSnappingSuspended => Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

    /// <summary>Runs the wall snapping for the pointer; <paramref name="previous"/> is the last placed point (null for the first one).</summary>
    private WallSnapResult RunWallSnap(Point raw, IReadOnlyList<SKPoint> placed)
    {
        var options = _context!.WallSnap;
        SKPoint? previous = placed.Count > 0 ? placed[^1] : null;
        var suspended = WallSnappingSuspended;
        var forceAxis = !suspended && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var snapOptions = suspended ? new WallSnapOptions { Enabled = false } : options;
        return WallSnapper.Snap(
            new SKPoint((float)raw.X, (float)raw.Y), previous, placed, _context.Document, snapOptions, AnchorSnapDip / Zoom, forceAxis);
    }

    /// <summary>While drawing a wall: jump to the start point when it is near (a click there closes the room), otherwise apply
    /// the wall snapping (corners, axis, alignment, equal length ...). Falls back to <paramref name="fallback"/> (grid, connection points).</summary>
    private Point SnapWallDrawPoint(Point raw, Point fallback, Shape wall)
    {
        if (IsNearWallStart(raw))
        {
            _wallClosePreview = true;
            _wallSnap = null;
            var (sx, sy) = WallStart(wall);
            return new Point(sx, sy);
        }

        var placed = WallGeometry.AbsolutePoints(wall);
        if (placed.Count > 0)
        {
            placed.RemoveAt(placed.Count - 1); // the moving ghost point
        }

        var result = RunWallSnap(raw, placed);
        _wallSnap = result;
        return result.Snapped ? new Point(result.Point.X, result.Point.Y) : fallback;
    }

    /// <summary>The first point of a wall: corners of other walls, midpoints, alignment.</summary>
    private Point SnapWallFirstPoint(Point raw)
    {
        var result = RunWallSnap(raw, []);
        _wallSnap = result;
        return result.Snapped ? new Point(result.Point.X, result.Point.Y) : raw;
    }

    /// <summary>Finds the wall under the pointer (front-most first) and the place on its centre line that would be cut.</summary>
    private (Shape Wall, Layer Layer, WallEditor.Spot Spot)? FindWallCutSpot(Point point)
    {
        if (_context is null)
        {
            return null;
        }

        foreach (var layer in _context.Document.Layers.Where(l => l.Visible))
        {
            foreach (var wall in layer.Shapes.Where(s => s.Kind == ShapeKind.Wall && s.Points is not null))
            {
                var spot = WallEditor.Nearest(wall, point.X, point.Y, 10 / Zoom);
                if (spot is { } found && found.Distance <= wall.WallThickness / 2 + 4 / Zoom)
                {
                    return (wall, layer, found);
                }
            }
        }

        return null;
    }

    private void HandleWallCutClick(Point point)
    {
        if (_context is null || FindWallCutSpot(point) is not { } hit)
        {
            return;
        }

        if (IsLocked(hit.Wall))
        {
            return;
        }

        var parts = WallEditor.SplitAt(hit.Wall, hit.Spot);
        if (parts.Count == 0)
        {
            return;
        }

        _context.ReplaceShape(hit.Wall, parts);
        _cutHover = null;
    }

    /// <summary>Marks the wall segment whose dimension is being edited in the DIMENSION card.</summary>
    private void DrawDimensionHighlight(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_context is not { WallSegmentHighlight: { } index } || _context.SelectedShapes.Count != 1 || _context.SelectedShapes[0] is not { Kind: ShapeKind.Wall } wall)
        {
            return;
        }

        var segments = WallGeometry.Segments(wall);
        if (index < 0 || index >= segments.Count)
        {
            return;
        }

        var (from, to) = segments[index];
        using var paint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Butt,
            StrokeWidth = (float)(wall.WallThickness + 8) * scaleX, Color = new SKColor(0xF3, 0x9C, 0x12, 110),
        };
        canvas.DrawLine(from.X * scaleX, from.Y * scaleY, to.X * scaleX, to.Y * scaleY, paint);
    }

    /// <summary>Guide lines, the snapped spot and a small label (length, angle, what it snapped to) while a wall is drawn.</summary>
    private void DrawWallSnap(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_wallSnap is not { } snap || _context is null)
        {
            return;
        }

        var drawing = _isPathDrawing && _pendingPathShape is { Kind: ShapeKind.Wall };
        var hovering = !_isPathDrawing && _context.ActiveTool == ActiveTool.PathDraw && _context.SelectedShapeKind == ShapeKind.Wall;
        if (!drawing && !hovering)
        {
            return;
        }

        var blue = new SKColor(0x5A, 0xB0, 0xFF);
        var green = new SKColor(0x2E, 0xCC, 0x71);
        var orange = MeasureOrange;

        foreach (var guide in snap.Guides)
        {
            var color = guide.Kind switch
            {
                SnapKind.Align => green,
                SnapKind.EqualLength or SnapKind.RoundLength => orange,
                _ => blue,
            };
            using var line = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = color.WithAlpha(210),
                PathEffect = SKPathEffect.CreateDash([5, 4], 0),
            };
            canvas.DrawLine(guide.From.X * scaleX, guide.From.Y * scaleY, guide.To.X * scaleX, guide.To.Y * scaleY, line);

            if (guide.Kind is SnapKind.EqualLength or SnapKind.RoundLength)
            {
                // small end ticks mark the length that was matched
                using var tick = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = color };
                var dx = guide.To.X - guide.From.X;
                var dy = guide.To.Y - guide.From.Y;
                var l = MathF.Sqrt(dx * dx + dy * dy);
                if (l > 1e-3f)
                {
                    var nx = -dy / l * 6;
                    var ny = dx / l * 6;
                    canvas.DrawLine(guide.To.X * scaleX - nx, guide.To.Y * scaleY - ny, guide.To.X * scaleX + nx, guide.To.Y * scaleY + ny, tick);
                }
            }
        }

        var px = snap.Point.X * scaleX;
        var py = snap.Point.Y * scaleY;
        if (snap.PointKind is SnapKind.Corner or SnapKind.Midpoint or SnapKind.OnWall)
        {
            using var mark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = snap.PointKind == SnapKind.Corner ? orange : green };
            if (snap.PointKind == SnapKind.Corner)
            {
                canvas.DrawRect(px - 6, py - 6, 12, 12, mark);
            }
            else if (snap.PointKind == SnapKind.Midpoint)
            {
                using var triangle = new SKPath();
                triangle.MoveTo(px, py - 7);
                triangle.LineTo(px + 7, py + 6);
                triangle.LineTo(px - 7, py + 6);
                triangle.Close();
                canvas.DrawPath(triangle, mark);
            }
            else
            {
                canvas.DrawCircle(px, py, 6, mark);
                canvas.DrawLine(px - 4, py - 4, px + 4, py + 4, mark);
                canvas.DrawLine(px - 4, py + 4, px + 4, py - 4, mark);
            }
        }

        if (snap.Label.Length > 0)
        {
            DrawPill(canvas, snap.Label, px + 14, py - 34);
        }
    }

    private void DrawWallHints(SKCanvas canvas, float scaleX, float scaleY)
    {
        DrawDimensionHighlight(canvas, scaleX, scaleY);
        DrawWallSnap(canvas, scaleX, scaleY);
        using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, Color = AccentColor };
        using var hot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = AccentColor };

        // Start point of the wall being drawn: lights up when a click would close the room.
        if (_isPathDrawing && _pendingPathShape is { Kind: ShapeKind.Wall } wall && wall.Points is { Count: > 1 })
        {
            var (sx, sy) = WallStart(wall);
            canvas.DrawCircle((float)sx * scaleX, (float)sy * scaleY, _wallClosePreview ? 9f : 5f, _wallClosePreview ? hot : ring);
        }

        // Cut tool: the place that would be cut.
        if (_context?.ActiveTool == ActiveTool.WallCut && _cutHover is { } spot)
        {
            using var cut = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = new SKColor(0xE5, 0x4B, 0x4B) };
            var cx = (float)spot.X * scaleX;
            var cy = (float)spot.Y * scaleY;
            canvas.DrawCircle(cx, cy, spot.IsVertex ? 8f : 6f, cut);
            canvas.DrawLine(cx - 11, cy - 11, cx + 11, cy + 11, cut);
            canvas.DrawLine(cx - 11, cy + 11, cx + 11, cy - 11, cut);
        }
    }

    // ----- Connection points (lines and arrows cling to a shape's 16 anchor spots) ----------------------

    private const double AnchorSnapDip = 10;

    /// <summary>Where the pointer is while a line is being placed or an end dragged; the anchors of shapes near it are drawn.</summary>
    private Point? _anchorProbe;

    private (Shape Shape, int Index)? _snappedAnchor;

    private bool IsLineToolActive => _context is { } c &&
        ((c.ActiveTool == ActiveTool.Shape && TwoPointLineKinds.Contains(c.SelectedShapeKind)) || c.ActiveTool == ActiveTool.PathDraw);

    /// <summary>The connection point close to <paramref name="point"/> (within a few screen pixels), or null. Also remembers
    /// the pointer and the hit so <see cref="DrawConnectionPoints"/> can show them.</summary>
    private Point? TryAnchorSnap(Point point, Shape? exclude)
    {
        if (_context is null)
        {
            return null;
        }

        _anchorProbe = point;
        var hit = ConnectionRouter.FindNearest(_context.Document, point.X, point.Y, AnchorSnapDip / Zoom, exclude);
        if (hit is null)
        {
            _snappedAnchor = null;
            return null;
        }

        _snappedAnchor = (hit.Value.Shape, hit.Value.Index);
        return new Point(hit.Value.X, hit.Value.Y);
    }

    private void ClearAnchorProbe()
    {
        if (_anchorProbe is null && _snappedAnchor is null && _wallSnap is null && _measureSnap is null)
        {
            return;
        }

        _wallSnap = null;
        _measureSnap = null;

        _anchorProbe = null;
        _snappedAnchor = null;
        SkiaCanvas.InvalidateVisual();
    }

    private void DrawConnectionPoints(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_context is null || _anchorProbe is not { } probe)
        {
            return;
        }

        var reach = 48 / Zoom;
        using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = AccentColor };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var hot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = AccentColor };

        foreach (var shape in ConnectionRouter.Targets(_context.Document))
        {
            if (probe.X < shape.X - reach || probe.X > shape.X + shape.Width + reach ||
                probe.Y < shape.Y - reach || probe.Y > shape.Y + shape.Height + reach)
            {
                continue;
            }

            for (var i = 0; i < ConnectionRouter.PointCount; i++)
            {
                var (px, py) = ConnectionRouter.GetPoint(shape, i);
                var cx = (float)px * scaleX;
                var cy = (float)py * scaleY;

                if (_snappedAnchor is { } snapped && ReferenceEquals(snapped.Shape, shape) && snapped.Index == i)
                {
                    canvas.DrawCircle(cx, cy, 7.5f, hot);
                    canvas.DrawCircle(cx, cy, 7.5f, ring);
                }
                else
                {
                    canvas.DrawCircle(cx, cy, 3.8f, fill);
                    canvas.DrawCircle(cx, cy, 3.8f, ring);
                }
            }
        }
    }

    /// <summary>True when the event came from the popup card itself (or from a drop-down opened by one of its combo boxes).</summary>
    private bool IsInsideQuickPopup(object? source)
    {
        for (var d = source as DependencyObject; d is not null;)
        {
            if (ReferenceEquals(d, LineQuickPopupControl))
            {
                return true;
            }

            // Drop-down lists live in their own popup window: follow the logical parent there, the visual parent elsewhere.
            d = (d as FrameworkElement)?.Parent ?? (d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d));
        }

        return false;
    }

    /// <summary>Opens the popup below the single selected line/arrow (above it when there is no room underneath).</summary>
    private void ShowLineQuickPopup()
    {
        if (_context is null || _context.ActiveTool != ActiveTool.Select || _context.SelectedShapes.Count != 1)
        {
            HideLineQuickPopup();
            return;
        }

        var shape = _context.SelectedShapes[0];
        if (!ShapeGeometryFactory.IsLineFamily(shape.Kind) || IsLocked(shape))
        {
            HideLineQuickPopup();
            return;
        }

        var popup = LineQuickPopupControl;
        popup.Bind(_context, shape);
        popup.Visibility = Visibility.Visible;
        popup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = popup.DesiredSize;

        const double gap = 18;
        var below = SkiaCanvas.TranslatePoint(new Point((shape.X + shape.Width / 2) * Zoom, (shape.Y + shape.Height) * Zoom + gap), QuickPopupLayer);
        var above = SkiaCanvas.TranslatePoint(new Point(0, shape.Y * Zoom - gap), QuickPopupLayer);

        var layerWidth = QuickPopupLayer.ActualWidth;
        var layerHeight = QuickPopupLayer.ActualHeight;
        var top = below.Y + size.Height <= layerHeight - 4 ? below.Y : Math.Max(4, above.Y - size.Height);
        var left = Math.Clamp(below.X - size.Width / 2, 4, Math.Max(4, layerWidth - size.Width - 4));

        Canvas.SetLeft(popup, left);
        Canvas.SetTop(popup, Math.Clamp(top, 4, Math.Max(4, layerHeight - size.Height - 4)));
    }

    private void UpdateCanvasFrameSize()
    {
        if (_context is null)
        {
            return;
        }

        CanvasFrame.Width = _context.Document.Width * Zoom;
        CanvasFrame.Height = _context.Document.Height * Zoom;
        CenterCanvasFrame();
    }

    private void CanvasFrameHost_OnSizeChanged(object sender, SizeChangedEventArgs e) => CenterCanvasFrame();

    /// <summary>The page sits in the middle of the work area (off-centre, in negative space, when it is larger than the area).</summary>
    private void CenterCanvasFrame()
    {
        Canvas.SetLeft(CanvasFrame, (CanvasFrameHost.ActualWidth - CanvasFrame.Width) / 2);
        Canvas.SetTop(CanvasFrame, (CanvasFrameHost.ActualHeight - CanvasFrame.Height) / 2);
    }

    /// <summary>Converts a point in this control's own DIP space (e.g. from
    /// <c>e.GetPosition(SkiaCanvas)</c>) into document-space coordinates. Distinct from the
    /// Skia-internal raster scale used only inside <see cref="SkiaCanvas_OnPaintSurface"/>.</summary>
    private Point ToDocumentSpace(Point viewPoint) => new(viewPoint.X / Zoom, viewPoint.Y / Zoom);

    // Cheap to call on every redraw — keeps the frame in sync whenever Document.Width/Height
    // changes (e.g. the Canvas Size panel), not just on Zoom changes.
    private void OnContextInvalidated(object? sender, EventArgs e)
    {
        UpdateCanvasFrameSize();
        SkiaCanvas.InvalidateVisual();
    }

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
        DrawConnectionPoints(canvas, scaleX, scaleY);
        DrawWallHints(canvas, scaleX, scaleY);
        DrawMeasure(canvas, scaleX, scaleY);
        DrawMatchSource(canvas, scaleX, scaleY);
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
        else if (selected.Count > 1 && selected.All(s => !IsLocked(s)))
        {
            DrawGroupResizeHandles(canvas, ComputeGroupBounds(selected), scaleX, scaleY);
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

        // Hug the drawn shape: the frame runs along its outer edge (half a border width outside the outline), no gap.
        var box = ChromeBox(shape);
        var pad = Math.Max(0, shape.BorderThickness / 2);
        var left = box.X - pad;
        var top = box.Y - pad;
        var right = box.X + box.W + pad;
        var bottom = box.Y + box.H + pad;

        var corners = new[] { new Point(left, top), new Point(right, top), new Point(right, bottom), new Point(left, bottom) };
        if (shape.Rotation != 0)
        {
            var pivot = new Point(shape.X + shape.Width / 2, shape.Y + shape.Height / 2);
            corners = corners.Select(c => RotateAroundCenter(c, pivot, shape.Rotation)).ToArray();
        }

        using var frame = new SKPath();
        frame.MoveTo((float)corners[0].X * scaleX, (float)corners[0].Y * scaleY);
        for (var i = 1; i < corners.Length; i++)
        {
            frame.LineTo((float)corners[i].X * scaleX, (float)corners[i].Y * scaleY);
        }

        frame.Close();
        canvas.DrawPath(frame, paint);
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

    /// <summary>The box the selection frame and its handles follow: the shape's DRAWN outline, so the frame sits right on the shape.
    /// (The icon templates keep a margin inside their X/Y/Width/Height frame; lines, text, images and grids already fill it.)</summary>
    private static (double X, double Y, double W, double H) ChromeBox(Shape shape)
    {
        return ShapeOutline.Box(shape);
    }

    /// <summary>Union bounding box (unrotated — a per-shape Rotation is ignored for this purpose,
    /// same simplification <see cref="DrawSelectionOutline"/> already makes) across every shape in
    /// a multi-selection, e.g. the several CombinedPath pieces a multi-element SVG import splits
    /// into. Lets a multi-selection be resized as one unit instead of offering no resize handles
    /// at all (the old behavior — <see cref="DrawResizeHandles"/> only ever ran for a lone shape).</summary>
    private static (double X, double Y, double W, double H) ComputeGroupBounds(IReadOnlyList<Shape> shapes)
    {
        var boxes = shapes.Select(ChromeBox).ToList();
        var minX = boxes.Min(b => b.X);
        var minY = boxes.Min(b => b.Y);
        var maxX = boxes.Max(b => b.X + b.W);
        var maxY = boxes.Max(b => b.Y + b.H);
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static IEnumerable<(ResizeHandle Handle, double X, double Y)> EnumerateBoundsHandlePositions(
        (double X, double Y, double W, double H) bounds)
    {
        var left = bounds.X;
        var top = bounds.Y;
        var right = bounds.X + bounds.W;
        var bottom = bounds.Y + bounds.H;
        var midX = bounds.X + bounds.W / 2;
        var midY = bounds.Y + bounds.H / 2;

        yield return (ResizeHandle.TopLeft, left, top);
        yield return (ResizeHandle.Top, midX, top);
        yield return (ResizeHandle.TopRight, right, top);
        yield return (ResizeHandle.Right, right, midY);
        yield return (ResizeHandle.BottomRight, right, bottom);
        yield return (ResizeHandle.Bottom, midX, bottom);
        yield return (ResizeHandle.BottomLeft, left, bottom);
        yield return (ResizeHandle.Left, left, midY);
    }

    /// <summary>Simplified sibling of <see cref="DrawResizeHandles"/> for a multi-selection — just
    /// the 8 corner/edge handles around the group's bounding box, no rotate or depth handle (those
    /// stay single-shape-only).</summary>
    private void DrawGroupResizeHandles(SKCanvas canvas, (double X, double Y, double W, double H) bounds, float scaleX, float scaleY)
    {
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var strokePaint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = AccentColor,
        };

        foreach (var (_, x, y) in EnumerateBoundsHandlePositions(bounds))
        {
            var rect = SKRect.Create(
                (float)x * scaleX - HandleScreenSize / 2, (float)y * scaleY - HandleScreenSize / 2,
                HandleScreenSize, HandleScreenSize);
            canvas.DrawRect(rect, fillPaint);
            canvas.DrawRect(rect, strokePaint);
        }
    }

    private static ResizeHandle? HitTestBoundsHandle(Point docPoint, (double X, double Y, double W, double H) bounds, double tolerance)
    {
        foreach (var (handle, x, y) in EnumerateBoundsHandlePositions(bounds))
        {
            if (Math.Abs(docPoint.X - x) <= tolerance && Math.Abs(docPoint.Y - y) <= tolerance)
            {
                return handle;
            }
        }

        return null;
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
    /// — a square handle per anchor point (draggable in both Select and Delete Point tools), plus
    /// a smaller "insert here" ghost dot at each segment midpoint — only shown for the dedicated
    /// Add Point tool, since Select itself no longer creates points.</summary>
    private void DrawPointHandles(SKCanvas canvas, Shape shape, float scaleX, float scaleY)
    {
        var points = shape.Points!;

        // Anchors render as SOLID filled circles and midpoint "click to add" ghosts as HOLLOW
        // dashed-ring circles — filled-vs-hollow is an unambiguous visual language for "this is a
        // real point you can drag" vs. "this is where a new point would go", so the point
        // situation reads clearly at a glance instead of every handle looking like the same square.
        using var anchorFillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = AccentColor };
        using var anchorRingPaint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = SKColors.White,
        };

        if (_context?.ActiveTool == ActiveTool.AddPoint)
        {
            using var ghostFillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White.WithAlpha(210) };
            using var ghostRingPaint = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = AccentColor,
                PathEffect = SKPathEffect.CreateDash([2, 2], 0),
            };

            for (var i = 0; i < points.Count - 1; i++)
            {
                var midX = (float)(shape.X + (points[i].X + points[i + 1].X) / 2) * scaleX;
                var midY = (float)(shape.Y + (points[i].Y + points[i + 1].Y) / 2) * scaleY;
                var midRadius = HandleScreenSize / 2.6f;
                canvas.DrawCircle(midX, midY, midRadius, ghostFillPaint);
                canvas.DrawCircle(midX, midY, midRadius, ghostRingPaint);
            }
        }

        foreach (var p in points)
        {
            var x = (float)(shape.X + p.X) * scaleX;
            var y = (float)(shape.Y + p.Y) * scaleY;
            var radius = HandleScreenSize / 2f;
            canvas.DrawCircle(x, y, radius, anchorFillPaint);
            canvas.DrawCircle(x, y, radius, anchorRingPaint);
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
        var box = ChromeBox(shape);
        var left = box.X;
        var top = box.Y;
        var right = box.X + box.W;
        var bottom = box.Y + box.H;
        var midX = box.X + box.W / 2;
        var midY = box.Y + box.H / 2;
        // a rotation turns the shape around the centre of its frame (not of the outline)
        var pivotX = shape.X + shape.Width / 2;
        var pivotY = shape.Y + shape.Height / 2;

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

        var center = new Point(pivotX, pivotY);
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
        var box = ChromeBox(shape);
        var basePoint = new Point(box.X + box.W / 2, box.Y - offset);

        if (shape.Rotation == 0)
        {
            return (basePoint.X, basePoint.Y);
        }

        var rotated = RotateAroundCenter(basePoint, new Point(shape.X + shape.Width / 2, shape.Y + shape.Height / 2), shape.Rotation);
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
        if (IsInsideQuickPopup(e.OriginalSource))
        {
            return;
        }

        HideLineQuickPopup();

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

        if (_context.ActiveTool == ActiveTool.WallCut)
        {
            HandleWallCutClick(point);
            return;
        }

        if (_context.ActiveTool == ActiveTool.Measure)
        {
            HandleRulerClick(point);
            return;
        }

        if (_context.ActiveTool == ActiveTool.MeasureGap)
        {
            HandleGapClick(point);
            return;
        }

        // Dedicated Add Point / Delete Point tools — same hit-testing as the Select tool's
        // built-in midpoint-click-to-add / Ctrl+click-to-delete gestures (see below), just
        // exposed as their own explicit modes so neither needs a modifier key or the Select
        // tool's overloaded click behavior to discover.
        if (_context.ActiveTool == ActiveTool.AddPoint)
        {
            if (_context.SelectedShapes.Count == 1 && !IsLocked(_context.SelectedShapes[0]) &&
                _context.SelectedShapes[0].Points is { Count: > 0 })
            {
                var pointShape = _context.SelectedShapes[0];
                if (HitTestPointHandle(point, pointShape) is { IsMidpoint: true } pointHit)
                {
                    InsertPointAt(pointShape, pointHit.Index, point);
                }
            }

            return;
        }

        if (_context.ActiveTool == ActiveTool.DeletePoint)
        {
            if (_context.SelectedShapes.Count == 1 && !IsLocked(_context.SelectedShapes[0]) &&
                _context.SelectedShapes[0].Points is { Count: > 0 })
            {
                var pointShape = _context.SelectedShapes[0];
                if (HitTestPointHandle(point, pointShape) is { IsMidpoint: false } pointHit &&
                    pointShape.Points!.Count > 2)
                {
                    RemovePointAt(pointShape, pointHit.Index);
                }
            }

            return;
        }

        if (_context.ActiveTool == ActiveTool.Eyedropper)
        {
            PickColorAt(point);
            return;
        }

        if (_context.ActiveTool == ActiveTool.Match)
        {
            HandleMatchClick(point);
            return;
        }

        if (_context.ActiveTool == ActiveTool.RegionColor)
        {
            HandleRegionColorClick(point);
            return;
        }

        // Select tool: a click on the number of a wall dimension opens the length editor.
        if (_context.ActiveTool == ActiveTool.Select && e.ClickCount == 1 && TryBeginWallLengthEdit(point))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            var dblHit = HitTestShape(point);
            if (dblHit is { } hitValue && hitValue.Shape.Kind == ShapeKind.Text && !IsLocked(hitValue.Shape))
            {
                BeginTextEdit(new Point(hitValue.Shape.X, hitValue.Shape.Y), hitValue.Shape);
                return;
            }

            // Double-click on any other shape: type text right into it.
            if (dblHit is { } labelHit && ShapeLabel.CanHaveLabel(labelHit.Shape) && !IsLocked(labelHit.Shape))
            {
                BeginLabelEdit(labelHit.Shape);
                return;
            }
        }

        var ctrlHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var soleSelectionEditable = _context.SelectedShapes.Count == 1 && !IsLocked(_context.SelectedShapes[0]);

        // Select only selects/moves — dragging an existing anchor point is still a "move", but
        // creating or deleting points is the dedicated Add Point / Delete Point tools' job now.
        if (soleSelectionEditable && _context.SelectedShapes[0].Points is { Count: > 0 })
        {
            var pointShape = _context.SelectedShapes[0];
            var pointHit = HitTestPointHandle(point, pointShape);

            if (pointHit is { IsMidpoint: false })
            {
                BeginDragPoint(pointShape, pointHit.Value.Index);
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
        else if (!ctrlHeld && _context.SelectedShapes.Count > 1 && _context.SelectedShapes.All(s => !IsLocked(s)))
        {
            var groupBounds = ComputeGroupBounds(_context.SelectedShapes);
            var groupTolerance = HandleHitToleranceDip / Zoom;
            if (HitTestBoundsHandle(point, groupBounds, groupTolerance) is { } groupHandle)
            {
                BeginGroupResize(groupHandle, groupBounds, point);
                return;
            }
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
                _moveStartPoint = point;
                _moveStartPositions = _context.SelectedShapes.Select(s => (s.X, s.Y)).ToList();
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
                if (shape.Kind == ShapeKind.DesignGrid)
                {
                    // The cell lookup undoes rotation and flips, so a rotated grid is hit exactly where it is drawn.
                    if (DesignGridMath.TryGetCell(shape, point.X, point.Y, out _, out _))
                    {
                        return (shape, layer);
                    }

                    continue;
                }

                if (shape.Kind == ShapeKind.Dimension)
                {
                    // Only its lines, arrowheads and the written value count - not the empty area between them.
                    if (DimensionRenderer.HitTest(shape, point.X, point.Y, 4 / Zoom))
                    {
                        return (shape, layer);
                    }

                    continue;
                }

                if (shape.Kind == ShapeKind.Wall)
                {
                    // A wall is hit on its band (not on the empty inside of a room or the corner of its bounding box).
                    var spot = WallEditor.Nearest(shape, point.X, point.Y, 0);
                    if (spot is { } onWall && onWall.Distance <= shape.WallThickness / 2 + 3 / Zoom)
                    {
                        return (shape, layer);
                    }

                    continue;
                }

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

    // ----- Design Grid painting (bucket tool) ----------------------------------------------------

    private bool _isGridPainting;
    private Shape? _gridPaintShape;
    private ShapeSnapshot _gridPaintBefore;
    private Dictionary<int, ArgbColor>? _gridPaintWorking;
    private bool _gridPaintErase;
    private bool _gridPaintChanged;
    private Point _gridPaintLast;

    /// <summary>Starts a bucket stroke on a design grid: the clicked square (and every square the mouse is then dragged
    /// over) takes the current fill colour — or is cleared when Alt is held. The whole stroke is one undo step.</summary>
    private void BeginGridPaint(Shape shape, Layer layer, Point point, int row, int col)
    {
        if (_context is null)
        {
            return;
        }

        _gridPaintShape = shape;
        _gridPaintBefore = ShapeSnapshot.Capture(shape);
        _gridPaintWorking = new Dictionary<int, ArgbColor>(shape.GridCells ?? new Dictionary<int, ArgbColor>());
        shape.GridCells = _gridPaintWorking;
        _gridPaintErase = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        _gridPaintChanged = false;
        _gridPaintLast = point;
        _isGridPainting = true;

        _context.SelectedLayer = layer;
        _context.SelectedShape = shape;
        CaptureMouse();

        PaintGridCell(row, col);
    }

    private void PaintGridCell(int row, int col)
    {
        if (_context is null || _gridPaintWorking is null)
        {
            return;
        }

        var key = DesignGridMath.Key(row, col);
        if (_gridPaintErase)
        {
            _gridPaintChanged |= _gridPaintWorking.Remove(key);
        }
        else if (!_gridPaintWorking.TryGetValue(key, out var existing) || !existing.Equals(_context.CurrentFillColor))
        {
            _gridPaintWorking[key] = _context.CurrentFillColor;
            _gridPaintChanged = true;
        }

        _context.RequestRedraw();
    }

    /// <summary>Continues a bucket stroke to <paramref name="point"/>, painting every square on the way (the mouse can jump
    /// several squares between two move events).</summary>
    private void ContinueGridPaint(Point point)
    {
        if (_gridPaintShape is null)
        {
            return;
        }

        var cell = Math.Max(0.5, _gridPaintShape.GridCellSize);
        var dx = point.X - _gridPaintLast.X;
        var dy = point.Y - _gridPaintLast.Y;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / (cell * 0.5)));

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            if (DesignGridMath.TryGetCell(_gridPaintShape, _gridPaintLast.X + dx * t, _gridPaintLast.Y + dy * t, out var row, out var col))
            {
                PaintGridCell(row, col);
            }
        }

        _gridPaintLast = point;
    }

    private void EndGridPaint()
    {
        _isGridPainting = false;
        ReleaseMouseCapture();

        if (_context is not null && _gridPaintShape is not null && _gridPaintChanged)
        {
            var after = ShapeSnapshot.Capture(_gridPaintShape);
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([_gridPaintShape], [_gridPaintBefore], [after]));
        }
        else if (_gridPaintShape is not null)
        {
            // Nothing changed: put the original dictionary back so no phantom edit is left behind.
            _gridPaintShape.GridCells = _gridPaintBefore.GridCells;
        }

        _gridPaintShape = null;
        _gridPaintWorking = null;
        _context?.RequestRedraw();
    }

    /// <summary>Moves the page back to the centre of the work area (after a pan).</summary>
    public void ResetView()
    {
        PanTransform.X = 0;
        PanTransform.Y = 0;
    }

    /// <summary>Eyedropper: takes the colour of the rendered page at <paramref name="point"/> (shapes, background and all) as the
    /// current paint colour, then returns to the Select tool. Transparent pixels pick nothing.</summary>
    private void PickColorAt(Point point)
    {
        if (_context is null)
        {
            return;
        }

        var document = _context.Document;
        var x = (int)Math.Floor(point.X);
        var y = (int)Math.Floor(point.Y);
        if (x < 0 || y < 0 || x >= document.Width || y >= document.Height)
        {
            return;
        }

        using var bitmap = DocumentExporter.RenderBitmap(document, document.Width, document.Height, includeBackground: true);
        var pixel = bitmap.GetPixel(x, y);
        if (pixel.Alpha == 0)
        {
            return;
        }

        _context.CurrentFillColor = new ArgbColor(pixel.Alpha, pixel.Red, pixel.Green, pixel.Blue);
        _context.ActiveTool = ActiveTool.Select;
    }

    private void BeginPlacing(Point point)
    {
        if (_context is null)
        {
            return;
        }

        if (TwoPointLineKinds.Contains(_context.SelectedShapeKind))
        {
            // A line or arrow starting near a shape's connection point clings to it.
            point = TryAnchorSnap(point, null) ?? point;
        }

        _isPlacing = true;
        _placeStart = point;

        if (TwoPointLineKinds.Contains(_context.SelectedShapeKind))
        {
            var kind = _context.SelectedShapeKind;
            _pendingShape = new Shape
            {
                Kind = kind,
                X = point.X,
                Y = point.Y,
                Width = 1,
                Height = 1,
                FillColor = _context.CurrentFillColor,
                Points = [new CorePoint(0, 0), new CorePoint(0, 0)],
                // The Arrow/Double Arrow tools' historical look — a plain open V head — is now just
                // the default for their LineStartHead/LineEndHead, freely reconfigurable afterwards
                // in the Properties panel (any Line-family shape can carry any head style).
                LineEndHead = kind is ShapeKind.Arrow or ShapeKind.DoubleArrow ? ArrowHeadKind.Open : ArrowHeadKind.None,
                LineStartHead = kind == ShapeKind.DoubleArrow ? ArrowHeadKind.Open : ArrowHeadKind.None,
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

            if (_pendingShape.Kind == ShapeKind.DesignGrid)
            {
                // Fine, light hairlines so the coloured squares stay the star of the grid.
                _pendingShape.BorderColor = ArgbColor.FromRgb(0xB4, 0xBA, 0xC4);
                _pendingShape.BorderThickness = 0.5;
            }
        }

        CaptureMouse();
    }

    private void BeginResize(ResizeHandle handle, Shape shape, Point point)
    {
        _isResizing = true;
        _resizeHandle = handle;
        _resizeShape = shape;
        _resizeStartRect = ChromeBox(shape);
        _resizeFrameStart = (shape.X, shape.Y, shape.Width, shape.Height);
        _resizeStartPoint = point;
        _resizeCenter = new Point(shape.X + shape.Width / 2, shape.Y + shape.Height / 2);
        _resizeRotation = shape.Rotation;
        _resizeUndoBefore = ShapeSnapshot.Capture(shape);
        CaptureMouse();
    }

    private void BeginGroupResize(ResizeHandle handle, (double X, double Y, double W, double H) bounds, Point point)
    {
        _isGroupResizing = true;
        _groupResizeHandle = handle;
        _groupResizeShapes = _context!.SelectedShapes.ToList();
        _groupResizeStartRects = _groupResizeShapes.Select(s => (s.X, s.Y, s.Width, s.Height)).ToList();
        _groupResizeUndoBefore = _groupResizeShapes.Select(ShapeSnapshot.Capture).ToList();
        _groupResizeStartBounds = bounds;
        _groupResizeStartPoint = point;
        CaptureMouse();
    }

    /// <summary>Scales every shape in the group proportionally within the resized bounding box —
    /// each shape's position/size relative to the ORIGINAL group bounds is preserved, so a
    /// multi-piece import (several CombinedPath shapes sharing one placement, or any other
    /// multi-shape selection) resizes as one coherent unit instead of each piece drifting apart.
    /// No rotation compensation (unlike the single-shape <see cref="ApplyResize"/>) — a reasonable
    /// simplification since the group bounds themselves are always axis-aligned.</summary>
    private void ApplyGroupResize(Point point)
    {
        var dx = point.X - _groupResizeStartPoint.X;
        var dy = point.Y - _groupResizeStartPoint.Y;
        var (startX, startY, startW, startH) = _groupResizeStartBounds;

        double newX = startX, newY = startY, newW = startW, newH = startH;

        var affectsLeft = _groupResizeHandle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        var affectsRight = _groupResizeHandle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        var affectsTop = _groupResizeHandle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        var affectsBottom = _groupResizeHandle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;

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

        // Dragging a CORNER keeps the proportions (no deformation) and scales both sides together, anchored at the
        // opposite corner. Hold Shift to stretch freely; the edge handles always stretch one side only.
        var keepProportions = KeepProportions(_groupResizeHandle) && startW > 0.0001 && startH > 0.0001;
        if (keepProportions)
        {
            var scale = Math.Max(newW / startW, newH / startH);
            scale = Math.Max(scale, Math.Max(MinShapeSize / startW, MinShapeSize / startH));

            newW = startW * scale;
            if (SnapIfEnabled(newW) is var snappedW && Math.Abs(snappedW - newW) > 0.0001 && snappedW >= MinShapeSize)
            {
                newW = snappedW;
                scale = newW / startW;
            }

            newH = startH * scale;
            newX = affectsLeft ? startX + startW - newW : startX;
            newY = affectsTop ? startY + startH - newH : startY;
        }

        if (!keepProportions)
        {
            newX = SnapIfEnabled(newX);
            newY = SnapIfEnabled(newY);
            newW = SnapIfEnabled(newW);
            newH = SnapIfEnabled(newH);
        }

        var safeStartW = startW > 0.0001 ? startW : 0.0001;
        var safeStartH = startH > 0.0001 ? startH : 0.0001;

        // Ratios MUST come from each shape's FROZEN starting rect, not its current (already
        // mutated-this-gesture) X/Y/Width/Height — reading live state here re-derives the ratios
        // from an already-resized rect on every subsequent mouse-move frame, compounding a drift
        // that made the shape visibly jump around while dragging.
        for (var i = 0; i < _groupResizeShapes.Count; i++)
        {
            var shape = _groupResizeShapes[i];
            var (shapeStartX, shapeStartY, shapeStartW, shapeStartH) = _groupResizeStartRects[i];

            var relX = (shapeStartX - startX) / safeStartW;
            var relY = (shapeStartY - startY) / safeStartH;
            var relW = shapeStartW / safeStartW;
            var relH = shapeStartH / safeStartH;

            shape.X = newX + relX * newW;
            shape.Y = newY + relY * newH;
            shape.Width = relW * newW;
            shape.Height = relH * newH;
        }
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
            point = TryAnchorSnap(point, null) ?? point;
            if (_context.SelectedShapeKind == ShapeKind.Wall)
            {
                point = SnapWallFirstPoint(point);
            }
            else if (_context.SelectedShapeKind == ShapeKind.Dimension)
            {
                point = SnapToMeasurable(point) ?? point;
            }

            _isPathDrawing = true;
            _pendingPathShape = new Shape
            {
                Kind = _context.SelectedShapeKind,
                X = point.X,
                Y = point.Y,
                Width = 1,
                Height = 1,
                FillColor = _context.CurrentFillColor,
                Points = [new CorePoint(0, 0), new CorePoint(0, 0)],
            };

            if (_pendingPathShape.Kind == ShapeKind.Dimension)
            {
                DimensionBuilder.ApplyStyle(_pendingPathShape);
                _pendingPathShape.DimKind = _context.CurrentDimensionKind;
                _pendingPathShape.DimUnit = _context.DimensionUnit;
            }

            if (_pendingPathShape.Kind == ShapeKind.Wall)
            {
                WallEditor.ApplyDrawingDefaults(_pendingPathShape);
            }

            _context.RequestRedraw();
            return;
        }

        // Clicking the start point again closes the wall into a ring.
        if (_pendingPathShape.Kind == ShapeKind.Wall && IsNearWallStart(point))
        {
            FinishPathDraw(closeWall: true);
            return;
        }

        if (clickCount >= 2 && _pendingPathShape.Kind != ShapeKind.Dimension)
        {
            FinishPathDraw();
            return;
        }

        var list = _pendingPathShape.Points!.ToList();
        list.Add(list[^1]);
        _pendingPathShape.Points = list;

        // A dimension is complete as soon as it has all the points it needs (the last one is the moving ghost).
        if (_pendingPathShape.Kind == ShapeKind.Dimension && list.Count - 1 >= DimensionPointsNeeded(_pendingPathShape.DimKind))
        {
            FinishPathDraw();
            return;
        }

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
        if (shape.Kind == ShapeKind.Wall)
        {
            WallEditor.RemapOverridesAfterInsert(shape, afterIndex);
        }

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
        if (shape.Kind == ShapeKind.Wall)
        {
            WallEditor.RemapOverridesBeforeRemove(shape, index);
        }

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

    /// <summary>Region Color's click handler: finds the front-most shape whose ACTUAL geometry
    /// (not just its bounding box — <see cref="HitTestShape"/> only checks that, which can't tell
    /// apart several same-bbox pieces, e.g. an SVG-imported icon's parts) contains the click point,
    /// then colors just the specific closed region that was clicked. An already-multi-piece
    /// CombinedPath shape (imported, or a previously split multi-region kind) just gets that one
    /// piece recolored. A not-yet-split shape with curated real sub-regions (Traffic Cone's
    /// cap/bands/base — see <see cref="ShapeGeometryFactory.GetColorableRegions"/>) gets split into
    /// one CombinedPath per region on first click, each keeping the original border look, and the
    /// clicked region takes the new color. Any other ordinary (single-region) shape just gets its
    /// whole FillColor set directly — this tool doubles as a quick "set fill and turn it on" click
    /// for normal shapes, not just multi-region ones.</summary>
    private void HandleRegionColorClick(Point point)
    {
        if (_context is null)
        {
            return;
        }

        foreach (var layer in _context.Document.Layers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            foreach (var shape in layer.Shapes)
            {
                if (shape.Kind == ShapeKind.DesignGrid)
                {
                    // Design grids are painted square by square (and not bounds-tested like the other shapes: the
                    // cell lookup already undoes rotation and flips).
                    if (!DesignGridMath.TryGetCell(shape, point.X, point.Y, out var gridRow, out var gridCol))
                    {
                        continue;
                    }

                    if (!IsLocked(shape))
                    {
                        BeginGridPaint(shape, layer, point, gridRow, gridCol);
                    }

                    return;
                }

                if (point.X < shape.X || point.X > shape.X + shape.Width ||
                    point.Y < shape.Y || point.Y > shape.Y + shape.Height)
                {
                    continue;
                }

                if (shape.Kind is ShapeKind.Box3D or ShapeKind.RasterImage or ShapeKind.Text)
                {
                    return;
                }

                if (shape.Kind != ShapeKind.CombinedPath)
                {
                    var curatedRegions = ShapeGeometryFactory.GetColorableRegions(shape.Kind);
                    if (curatedRegions is not null)
                    {
                        using var whole = ShapeGeometryFactory.Build(shape);
                        if (!whole.Contains((float)point.X, (float)point.Y))
                        {
                            continue;
                        }

                        if (!IsLocked(shape))
                        {
                            ExplodeShapeIntoRegions(shape, layer, curatedRegions, point);
                        }

                        return;
                    }
                }

                using var built = ShapeGeometryFactory.Build(shape);
                if (!built.Contains((float)point.X, (float)point.Y))
                {
                    // Bbox matched but the click missed this piece's actual ink — a stacked
                    // sibling piece under it might still match, so keep scanning.
                    continue;
                }

                if (!IsLocked(shape))
                {
                    var before = ShapeSnapshot.Capture(shape);
                    shape.FillColor = _context.CurrentFillColor;
                    shape.FillEnabled = true;
                    if (shape.Kind == ShapeKind.CombinedPath)
                    {
                        shape.CustomPathStrokeOnly = false;
                    }

                    var after = ShapeSnapshot.Capture(shape);
                    _context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [after]));

                    _context.SelectedShape = shape;
                    _context.SelectedLayer = layer;
                    _context.RequestRedraw();
                }

                return;
            }
        }
    }

    /// <summary>Replaces a not-yet-split shape with one <see cref="ShapeKind.CombinedPath"/> per
    /// curated region (see <see cref="ShapeGeometryFactory.GetColorableRegions"/>), all sharing the
    /// original's placement/rotation/border so nothing shifts, then colors whichever region was
    /// actually clicked (smallest-area containing match, so a small piece wins over a larger one it
    /// happens to overlap, e.g. Traffic Cone's base plate slightly overlapping its bottom band).</summary>
    private void ExplodeShapeIntoRegions(Shape shape, Layer layer, IReadOnlyList<string> regionPathData, Point clickPoint)
    {
        if (_context is null)
        {
            return;
        }

        var originalIndex = layer.Shapes.IndexOf(shape);
        if (originalIndex < 0)
        {
            return;
        }

        var hitIndex = 0;
        var hitArea = float.MaxValue;
        for (var i = 0; i < regionPathData.Count; i++)
        {
            var probe = new Shape
            {
                Kind = ShapeKind.CombinedPath,
                X = shape.X,
                Y = shape.Y,
                Width = shape.Width,
                Height = shape.Height,
                Rotation = shape.Rotation,
                FlipHorizontal = shape.FlipHorizontal,
                FlipVertical = shape.FlipVertical,
                CustomPathData = regionPathData[i],
            };

            using var probePath = ShapeGeometryFactory.Build(probe);
            if (!probePath.Contains((float)clickPoint.X, (float)clickPoint.Y))
            {
                continue;
            }

            var area = probePath.Bounds.Width * probePath.Bounds.Height;
            if (area < hitArea)
            {
                hitArea = area;
                hitIndex = i;
            }
        }

        layer.Shapes.RemoveAt(originalIndex);
        var combinedActions = new List<UndoableAction> { UndoableAction.ForRemove([(layer.Shapes, shape, originalIndex)]) };

        var newShapes = new List<Shape>();
        for (var i = 0; i < regionPathData.Count; i++)
        {
            var piece = new Shape
            {
                Kind = ShapeKind.CombinedPath,
                X = shape.X,
                Y = shape.Y,
                Width = shape.Width,
                Height = shape.Height,
                Rotation = shape.Rotation,
                FlipHorizontal = shape.FlipHorizontal,
                FlipVertical = shape.FlipVertical,
                Opacity = shape.Opacity,
                FillColor = i == hitIndex ? _context.CurrentFillColor : shape.FillColor,
                FillEnabled = true,
                BorderColor = shape.BorderColor,
                BorderThickness = shape.BorderThickness,
                CustomPathData = regionPathData[i],
                CustomPathStrokeOnly = false,
            };

            newShapes.Add(piece);
            layer.Shapes.Insert(originalIndex + i, piece);
        }

        for (var i = 0; i < newShapes.Count; i++)
        {
            combinedActions.Add(UndoableAction.ForInsert(layer.Shapes, newShapes[i], originalIndex + i));
        }

        _context.UndoManager.Push(UndoableAction.Combine(combinedActions));

        _context.SelectedShape = newShapes[hitIndex];
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
            Background = (Brush)FindResource("InsetBrush"),
            Foreground = (Brush)FindResource("ForegroundTextBrush"),
            BorderBrush = (Brush)FindResource("AccentBlueMidBrush"),
            CaretBrush = (Brush)FindResource("AccentBlueMidBrush"),
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

    /// <summary>Double-click on a shape: opens an in-place editor over the shape. Enter starts a new line,
    /// Ctrl+Enter or a click elsewhere keeps the text, Esc discards the edit.</summary>
    private void BeginLabelEdit(Shape shape)
    {
        if (_context is null)
        {
            return;
        }

        CancelTextEdit();

        _textEditShape = shape;
        _textEditLayer = _context.Document.Layers.FirstOrDefault(l => l.Shapes.Contains(shape));
        _context.SelectedShape = shape;

        var box = ConnectionRouter.GetBox(shape);
        var fontSize = string.IsNullOrEmpty(shape.TextContent) ? ShapeLabel.SuggestedFontSize(shape) : shape.TextFontSize;
        var textColor = ShapeLabel.EffectiveColor(shape);
        var surface = shape.FillEnabled ? shape.FillColor : ArgbColor.FromRgb(0xFF, 0xFF, 0xFF);

        var textBox = new TextBox
        {
            Text = shape.TextContent ?? string.Empty,
            FontSize = fontSize * Zoom,
            FontFamily = new FontFamily(string.IsNullOrWhiteSpace(shape.TextFontFamily) ? "Segoe UI" : shape.TextFontFamily),
            FontWeight = shape.TextBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = shape.TextItalic ? FontStyles.Italic : FontStyles.Normal,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4),
            Width = Math.Max(72, box.Width * Zoom),
            MinHeight = Math.Max(box.Height * Zoom, fontSize * Zoom * 1.9),
            Background = new SolidColorBrush(Color.FromRgb(surface.R, surface.G, surface.B)),
            Foreground = new SolidColorBrush(Color.FromRgb(textColor.R, textColor.G, textColor.B)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(textColor.R, textColor.G, textColor.B)),
            BorderBrush = (Brush)FindResource("AccentBlueMidBrush"),
            BorderThickness = new Thickness(1),
        };

        var width = textBox.Width;
        var center = SkiaCanvas.TranslatePoint(new Point((box.Left + box.Width / 2) * Zoom, (box.Top + box.Height / 2) * Zoom), TextEditOverlay);
        Canvas.SetLeft(textBox, center.X - width / 2);
        Canvas.SetTop(textBox, center.Y - textBox.MinHeight / 2);

        TextEditOverlay.Children.Add(textBox);
        _textEditBox = textBox;

        textBox.KeyDown += TextEditBox_OnKeyDown;
        textBox.LostFocus += TextEditBox_OnLostFocus;

        textBox.Focus();
        Keyboard.Focus(textBox);
        textBox.SelectAll();
    }

    /// <summary>Stores the typed text on the shape (empty text removes the label) as one undo step.</summary>
    private void CommitLabelEdit()
    {
        if (_textEditBox is null || _textEditShape is not { } shape || _context is null)
        {
            return;
        }

        var text = _textEditBox.Text.TrimEnd();
        RemoveTextEditBox();

        var before = ShapeSnapshot.Capture(shape);
        var hadText = !string.IsNullOrEmpty(shape.TextContent);
        shape.TextContent = string.IsNullOrWhiteSpace(text) ? null : text;
        if (!hadText && shape.TextContent is not null)
        {
            shape.TextFontSize = ShapeLabel.SuggestedFontSize(shape);
        }

        var after = ShapeSnapshot.Capture(shape);
        if (!after.Equals(before))
        {
            _context.UndoManager.Push(UndoableAction.ForPropertyEdit([shape], [before], [after]));
        }

        _context.RequestRedraw();
    }

    private void TextEditBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        var isLabel = _textEditShape is { Kind: not ShapeKind.Text };

        // In a shape's label Enter is a line break; Ctrl+Enter finishes (a Text shape is a single line: Enter finishes).
        if (e.Key == Key.Enter && (!isLabel || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
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

    /// <summary>Keeps the text being typed (in a shape or a text shape) - called before anything else the user does, such as a
    /// click outside the editor, a menu command or Ctrl+S.</summary>
    public void CommitActiveTextEdit()
    {
        CommitWallLength();
        CommitTextEdit();
    }

    /// <summary>Like <see cref="CommitActiveTextEdit"/>, but a click inside the editor itself changes nothing.</summary>
    public void CommitTextEditUnlessInside(object? source)
    {
        CommitWallLengthUnlessInside(source);

        if (_textEditBox is null)
        {
            return;
        }

        for (var d = source as DependencyObject; d is not null;)
        {
            if (ReferenceEquals(d, _textEditBox))
            {
                return;
            }

            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        CommitTextEdit();
    }

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

        if (_textEditShape is { Kind: not ShapeKind.Text })
        {
            CommitLabelEdit();
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

        using var font = new SKFont(FontCache.Get("Segoe UI", bold, italic), (float)fontSize);
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
                // Text has no outline until the user adds one in the BORDER card.
                BorderThickness = 0,
                TextContent = text,
            };

            layer.Shapes.Insert(0, shape);
            _context.UndoManager.Push(UndoableAction.ForInsert(layer.Shapes, shape, 0));
            _context.SelectedShape = shape;
            _context.SelectedLayer = layer;
            // Same rule as shape placement: after creating new text, drop back to Select so a
            // second text shape requires deliberately choosing the Text tool again.
            _context.ActiveTool = ActiveTool.Select;
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
        if (_context?.ActiveTool == ActiveTool.Measure && !IsInsideQuickPopup(e.OriginalSource))
        {
            UpdateRulerHover(ToDocumentSpace(e.GetPosition(SkiaCanvas)));
        }
        else if (_context?.ActiveTool == ActiveTool.MeasureGap && !IsInsideQuickPopup(e.OriginalSource))
        {
            UpdateGapHover(ToDocumentSpace(e.GetPosition(SkiaCanvas)));
        }

        if (_context?.ActiveTool == ActiveTool.WallCut && !IsInsideQuickPopup(e.OriginalSource))
        {
            var hover = FindWallCutSpot(ToDocumentSpace(e.GetPosition(SkiaCanvas)));
            _cutHover = hover?.Spot;
            SkiaCanvas.InvalidateVisual();
        }
        else if (_cutHover is not null)
        {
            _cutHover = null;
                SkiaCanvas.InvalidateVisual();
        }

        if (IsLineToolActive && !_isPlacing && !_isPathDrawing && !IsInsideQuickPopup(e.OriginalSource))
        {
            // Hovering with a line tool: show the connection points of nearby shapes so the user sees where it will cling.
            var hoverPoint = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            var before = _snappedAnchor;
            TryAnchorSnap(hoverPoint, null);
            if (_context?.SelectedShapeKind == ShapeKind.Wall)
            {
                _wallSnap = RunWallSnap(hoverPoint, []);
            }
            else if (_context?.SelectedShapeKind == ShapeKind.Dimension)
            {
                SnapToMeasurable(hoverPoint);
            }

            if (before != _snappedAnchor || _anchorProbe is not null)
            {
                SkiaCanvas.InvalidateVisual();
            }
        }
        else if (!IsLineToolActive && !_isPlacing && !_isPathDrawing && !_isDraggingPoint)
        {
            ClearAnchorProbe();
        }

        if (_isGridPainting)
        {
            ContinueGridPaint(ToDocumentSpace(e.GetPosition(SkiaCanvas)));
            return;
        }

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
                point = TryAnchorSnap(point, _pendingShape) ?? point;
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
            var rawPoint = point;
            point = TryAnchorSnap(point, null) ?? new Point(SnapIfEnabled(point.X), SnapIfEnabled(point.Y));
            _wallClosePreview = false;
            if (_pendingPathShape.Kind == ShapeKind.Wall)
            {
                point = SnapWallDrawPoint(rawPoint, point, _pendingPathShape);
            }
            else if (_pendingPathShape.Kind == ShapeKind.Dimension)
            {
                point = SnapToMeasurable(rawPoint) ?? point;
            }

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
            var isEnd = draggedIndex == 0 || draggedIndex == _pointEditShape.Points!.Count - 1;
            point = (isEnd ? TryAnchorSnap(point, _pointEditShape) : null) ?? new Point(SnapIfEnabled(point.X), SnapIfEnabled(point.Y));
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

        if (_isGroupResizing)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            ApplyGroupResize(point);
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

        if (_isMovingSelection && _context is not null && _moveStartPositions.Count == _context.SelectedShapes.Count)
        {
            var point = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            // Total delta from the FROZEN drag-start point, applied to each shape's FROZEN start
            // position — not an incremental "+= dx" every frame. Snapping the incremental form
            // permanently rounds shape.X/Y away on every single mouse-move event, discarding the
            // sub-grid remainder of the true cursor movement each time; with a grid coarser than a
            // typical per-frame mouse delta, that remainder never accumulates enough to cross a
            // grid line, so the shape looked frozen in place. Recomputing from the start each frame
            // keeps the true (unsnapped) position intact for the snap math to round correctly.
            var totalDx = point.X - _moveStartPoint.X;
            var totalDy = point.Y - _moveStartPoint.Y;

            for (var i = 0; i < _context.SelectedShapes.Count; i++)
            {
                var shape = _context.SelectedShapes[i];
                var (startX, startY) = _moveStartPositions[i];
                shape.X = startX + totalDx;
                shape.Y = startY + totalDy;
            }

            // Snapping a multi-shape drag would distort relative offsets between shapes, so only
            // snap when moving a single shape.
            if (_context.SelectedShapes.Count == 1)
            {
                var shape = _context.SelectedShapes[0];
                shape.X = SnapIfEnabled(shape.X);
                shape.Y = SnapIfEnabled(shape.Y);
            }

            _context.RequestRedraw();
            return;
        }

        if (_isMarqueeSelecting)
        {
            _marqueeCurrent = ToDocumentSpace(e.GetPosition(SkiaCanvas));
            _context?.RequestRedraw();
        }
    }

    /// <summary>Corner handles keep the aspect ratio (so imported SVGs, images and every other shape are never
    /// deformed by a corner drag); Shift lifts that and stretches freely.</summary>
    private static bool KeepProportions(ResizeHandle handle) =>
        handle is ResizeHandle.TopLeft or ResizeHandle.TopRight or ResizeHandle.BottomLeft or ResizeHandle.BottomRight
        && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

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

        // Dragging a CORNER keeps the proportions (no deformation) and scales both sides together, anchored at the
        // opposite corner. Hold Shift to stretch freely; the edge handles always stretch one side only.
        var keepProportions = KeepProportions(_resizeHandle) && startW > 0.0001 && startH > 0.0001;
        if (keepProportions)
        {
            var scale = Math.Max(newW / startW, newH / startH);
            scale = Math.Max(scale, Math.Max(MinShapeSize / startW, MinShapeSize / startH));

            newW = startW * scale;
            if (SnapIfEnabled(newW) is var snappedW && Math.Abs(snappedW - newW) > 0.0001 && snappedW >= MinShapeSize)
            {
                newW = snappedW;
                scale = newW / startW;
            }

            newH = startH * scale;
            newX = affectsLeft ? startX + startW - newW : startX;
            newY = affectsTop ? startY + startH - newH : startY;
        }

        // Proportional corner drags were already snapped as a whole above (snapping X/Y/H separately would break the ratio).
        var outlineX = keepProportions ? newX : SnapIfEnabled(newX);
        var outlineY = keepProportions ? newY : SnapIfEnabled(newY);
        var outlineW = keepProportions ? newW : SnapIfEnabled(newW);
        var outlineH = keepProportions ? newH : SnapIfEnabled(newH);

        // The handles sit on the drawn outline, which is inset in the shape's X/Y/Width/Height frame by a fixed fraction;
        // turn the dragged outline back into that frame.
        var (frameX, frameY, frameW, frameH) = _resizeFrameStart;
        var ratioW = frameW > 0.0001 ? Math.Max(0.0001, startW / frameW) : 1;
        var ratioH = frameH > 0.0001 ? Math.Max(0.0001, startH / frameH) : 1;
        var insetX = frameW > 0.0001 ? (startX - frameX) / frameW : 0;
        var insetY = frameH > 0.0001 ? (startY - frameY) / frameH : 0;
        var newFrameW = outlineW / ratioW;
        var newFrameH = outlineH / ratioH;
        _resizeShape.X = outlineX - insetX * newFrameW;
        _resizeShape.Y = outlineY - insetY * newFrameH;
        _resizeShape.Width = newFrameW;
        _resizeShape.Height = newFrameH;
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
        if (IsInsideQuickPopup(e.OriginalSource))
        {
            return;
        }

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

        if (_isGridPainting)
        {
            EndGridPaint();
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

        if (_isGroupResizing)
        {
            _isGroupResizing = false;
            ReleaseMouseCapture();

            if (_context is not null && _groupResizeShapes.Count == _groupResizeUndoBefore.Count)
            {
                var after = _groupResizeShapes.Select(ShapeSnapshot.Capture).ToList();
                if (!after.SequenceEqual(_groupResizeUndoBefore))
                {
                    _context.UndoManager.Push(
                        UndoableAction.ForPropertyEdit(_groupResizeShapes, _groupResizeUndoBefore, after));
                }
            }

            _groupResizeShapes = [];
            _groupResizeStartRects = [];
            _groupResizeUndoBefore = [];
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
                if (ShapeGeometryFactory.IsLineFamily(_pointEditShape.Kind))
                {
                    // An end dropped exactly on a connection point is glued to it; any other end is free.
                    ConnectionRouter.AttachEnds(_context.Document, _pointEditShape);
                }

                if (_pointEditShape.Kind == ShapeKind.Dimension)
                {
                    DimensionRouter.AttachPoints(_context.Document, _pointEditShape);
                }

                var after = ShapeSnapshot.Capture(_pointEditShape);
                if (!after.Equals(_pointDragUndoBefore))
                {
                    _context.UndoManager.Push(
                        UndoableAction.ForPropertyEdit([_pointEditShape], [_pointDragUndoBefore], [after]));
                }
            }

            _pointEditShape = null;
            _draggedPointIndex = null;
            ClearAnchorProbe();
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
            _moveStartPositions = [];
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
        if (!_isPlacing && !_isPathDrawing && !_isDraggingPoint)
        {
            ClearAnchorProbe();
        }

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

        if (shape is { Kind: ShapeKind.DesignGrid })
        {
            // A grid always consists of whole squares; a plain click (no real drag) drops a 24 x 24 grid.
            var cell = shape.GridCellSize;
            var clickOnly = shape.Width < cell * 2 && shape.Height < cell * 2;
            shape.Width = (clickOnly ? 24 : Math.Max(1, Math.Round(shape.Width / cell))) * cell;
            shape.Height = (clickOnly ? 24 : Math.Max(1, Math.Round(shape.Height / cell))) * cell;
        }

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
                if (shape.Points is not null)
                {
                    ConnectionRouter.AttachEnds(_context.Document, shape);
                }

                // After placing a shape, drop back to Select so the user reviews/moves it rather
                // than immediately stamping out another one — placing a second shape requires
                // choosing the Shape tool again.
                _context.ActiveTool = ActiveTool.Select;
            }
        }

        _pendingShape = null;
        ClearAnchorProbe();
        _context?.RequestRedraw();
    }
}
