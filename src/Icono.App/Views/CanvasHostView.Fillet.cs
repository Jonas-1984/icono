using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.App.Views;

/// <summary>The Curve tool, like the fillet of a CAD program: click one side of a corner, click the neighbouring side, type the Curve size in the small
/// box and press Enter - only that corner is curved.</summary>
public partial class CanvasHostView
{
    private (Shape Shape, int Side)? _filletFirst;
    private (Shape Shape, int Side)? _filletSecond;
    private (Shape Shape, int Side)? _filletHover;
    private int _filletCorner;
    private TextBox? _filletBox;

    private float FilletTolerance => (float)(10 / Zoom);

    /// <summary>One click of the Curve tool: the first click picks a side, the second the neighbouring side (and opens the size box).</summary>
    private void HandleCurveClick(Point point)
    {
        if (_context is null)
        {
            return;
        }

        CancelFilletBox();
        var at = new SKPoint((float)point.X, (float)point.Y);

        if (_filletFirst is not { } first)
        {
            var shape = HitTestShape(point)?.Shape;
            if (shape is null)
            {
                return;
            }

            if (IsLocked(shape))
            {
                _context.Notify("That object is locked.");
                return;
            }

            if (CornerFillet.NearestSide(shape, at, FilletTolerance) is not { } side)
            {
                _context.Notify(ShapeGeometryFactory.CanRoundCorners(shape)
                    ? "Curve: click on one side of the corner (on the line itself)."
                    : "This object has no corners to curve: use a line with three points or more, or a shape with straight sides.");
                return;
            }

            _filletFirst = (shape, side);
            _context.SelectedShape = shape;
            _context.Notify("Curve: now click the neighbouring side of the corner. Esc cancels.");
            SkiaCanvas.InvalidateVisual();
            return;
        }

        if (CornerFillet.NearestSide(first.Shape, at, FilletTolerance) is not { } second)
        {
            _context.Notify("Curve: click the side next to the first one (on the same object).");
            return;
        }

        var vertices = CornerFillet.Vertices(first.Shape);
        var corner = vertices is null ? null : CornerFillet.SharedCorner(vertices.Count, CornerFillet.IsClosed(first.Shape), first.Side, second);
        if (corner is not { } found || vertices is null)
        {
            _context.Notify("These two sides do not meet in a corner. Click the side right next to the first one.");
            return;
        }

        _filletSecond = (first.Shape, second);
        _filletCorner = found;
        OpenFilletBox(vertices[found]);
        SkiaCanvas.InvalidateVisual();
    }

    private void OpenFilletBox(SKPoint corner)
    {
        if (_context is null)
        {
            return;
        }

        var box = new TextBox
        {
            Text = _context.CurveSizeCm.ToString("0.##", CultureInfo.InvariantCulture),
            Width = 84,
            Height = 26,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBlueMidBrush"),
            BorderThickness = new Thickness(1.5),
            ToolTip = "Curve size in cm: type a number and press Enter (0 makes the corner sharp). Esc cancels.",
        };
        System.Windows.Automation.AutomationProperties.SetName(box, "Curve size in cm");

        var at = SkiaCanvas.TranslatePoint(new Point(corner.X * Zoom + 14, corner.Y * Zoom + 12), TextEditOverlay);
        Canvas.SetLeft(box, at.X);
        Canvas.SetTop(box, at.Y);
        TextEditOverlay.Children.Add(box);
        _filletBox = box;
        box.KeyDown += FilletBox_OnKeyDown;
        box.LostFocus += FilletBox_OnLostFocus;
        box.Focus();
        Keyboard.Focus(box);
        box.SelectAll();
        _context.Notify("Curve: type the Curve size in cm, then Enter. Esc cancels.");
    }

    private void FilletBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFillet();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelFillet();
            e.Handled = true;
        }
    }

    private void FilletBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        // a click elsewhere gives the box up (a click on the canvas then starts a new pick)
        if (_filletBox is not null && ReferenceEquals(sender, _filletBox))
        {
            CancelFillet();
        }
    }

    private void CommitFillet()
    {
        if (_filletBox is not { } box || _filletSecond is not { } picked || _context is null)
        {
            return;
        }

        var text = box.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cm) || cm < 0)
        {
            _context.Notify("That is not a size. Type a number in cm, for example 1.5.");
            box.SelectAll();
            return;
        }

        var shape = picked.Shape;
        var corner = _filletCorner;
        CancelFillet();
        if (cm > 0)
        {
            _context.CurveSizeCm = cm;
        }

        _context.FilletCorner(shape, corner, cm);
        Focus();
    }

    /// <summary>Drops the picked sides and the size box. True when there was something to drop (the first Esc of the tool).</summary>
    public bool CancelFillet()
    {
        var had = _filletFirst is not null || _filletBox is not null;
        CancelFilletBox();
        _filletFirst = null;
        _filletSecond = null;
        _filletHover = null;
        if (had)
        {
            SkiaCanvas.InvalidateVisual();
        }

        return had;
    }

    private void CancelFilletBox()
    {
        if (_filletBox is not { } box)
        {
            return;
        }

        _filletBox = null;
        box.KeyDown -= FilletBox_OnKeyDown;
        box.LostFocus -= FilletBox_OnLostFocus;
        TextEditOverlay.Children.Remove(box);
        _filletSecond = null;
    }

    /// <summary>The side under the pointer lights up while the Curve tool is active, so it is clear what a click would pick.</summary>
    private void UpdateFilletHover(Point point)
    {
        if (_context is not { ActiveTool: ActiveTool.CornerCurve } || _filletBox is not null)
        {
            if (_filletHover is not null)
            {
                _filletHover = null;
                SkiaCanvas.InvalidateVisual();
            }

            return;
        }

        (Shape, int)? hover = null;
        var shape = _filletFirst?.Shape ?? HitTestShape(point)?.Shape;
        if (shape is not null && !IsLocked(shape)
            && CornerFillet.NearestSide(shape, new SKPoint((float)point.X, (float)point.Y), FilletTolerance) is { } side)
        {
            hover = (shape, side);
        }

        if (hover != _filletHover)
        {
            _filletHover = hover;
            SkiaCanvas.InvalidateVisual();
        }
    }

    private void DrawFilletPicks(SKCanvas canvas, float scaleX, float scaleY)
    {
        if (_context is not { ActiveTool: ActiveTool.CornerCurve })
        {
            return;
        }

        void Side((Shape Shape, int Side) pick, SKColor color, float width)
        {
            if (CornerFillet.Vertices(pick.Shape) is not { } vertices || pick.Side >= CornerFillet.SideCount(vertices.Count, CornerFillet.IsClosed(pick.Shape)))
            {
                return;
            }

            var (a, b) = CornerFillet.Ends(vertices, pick.Side);
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, Color = color };
            canvas.DrawLine(a.X * scaleX, a.Y * scaleY, b.X * scaleX, b.Y * scaleY, paint);
        }

        if (_filletHover is { } hover && _filletFirst != hover)
        {
            Side(hover, MeasureOrange.WithAlpha(150), 3f);
        }

        if (_filletFirst is { } first)
        {
            Side(first, MeasureOrange, 3.5f);
        }

        if (_filletSecond is { } second)
        {
            Side(second, MeasureOrange, 3.5f);
            if (CornerFillet.Vertices(second.Shape) is { } vertices && _filletCorner < vertices.Count)
            {
                var corner = vertices[_filletCorner];
                using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = MeasureOrange };
                canvas.DrawCircle(corner.X * scaleX, corner.Y * scaleY, 8f, ring);
            }
        }
    }
}
