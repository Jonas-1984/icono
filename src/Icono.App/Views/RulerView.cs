using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Icono.Core;

namespace Icono.App.Views;

/// <summary>A ruler along the top or the left edge of the work area. It shows real-world lengths (page length x drawing scale) in the unit of the
/// dimensions, with zero at the corner of every page; a mark follows the pointer. The canvas tells it where the pages are (<see cref="Set"/>).</summary>
public sealed class RulerView : FrameworkElement
{
    /// <summary>Width of a vertical ruler, height of a horizontal one.</summary>
    public const double Thickness = 22;

    private double _origin;
    private double _zoom = 1;
    private Document? _document;
    private LengthUnit _unit = LengthUnit.Centimeter;
    private double? _cursor;

    /// <summary>True for the ruler on the left (it measures y), false for the one on top (it measures x).</summary>
    public bool Vertical { get; set; }

    /// <summary>Where page 0 starts on the screen along the ruler (DIP from the start of the ruler), the zoom (screen DIP per page px) and
    /// the document (page size and number, drawing scale).</summary>
    public void Set(double origin, double zoom, Document document, LengthUnit unit)
    {
        if (_origin == origin && _zoom == zoom && ReferenceEquals(_document, document) && _unit == unit
            && _pages == document.PageCount && _pageSize == (document.Width, document.Height) && _scale == document.ScaleDenominator)
        {
            return;
        }

        _origin = origin;
        _zoom = zoom;
        _document = document;
        _unit = unit;
        _pages = document.PageCount;
        _pageSize = (document.Width, document.Height);
        _scale = document.ScaleDenominator;
        InvalidateVisual();
    }

    private int _pages;
    private (int, int) _pageSize;
    private double _scale;

    /// <summary>The pointer position along the ruler's axis in page px (null = pointer not on the page area).</summary>
    public void SetCursor(double? position)
    {
        if (_cursor == position)
        {
            return;
        }

        _cursor = position;
        InvalidateVisual();
    }

    /// <summary>Page px for one unit of real length at the drawing scale (a centimetre is 96 / 2.54 px on a 1:1 page).</summary>
    private double PagePxPerUnit()
    {
        var perUnit = _unit switch
        {
            LengthUnit.Millimeter => 96.0 / 25.4,
            LengthUnit.Meter => 96.0 / 0.0254,
            LengthUnit.Inch => 96.0,
            _ => 96.0 / 2.54,
        };
        return perUnit / (_scale > 0 ? _scale : 1);
    }

    public static string UnitLabel(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => "mm",
        LengthUnit.Meter => "m",
        LengthUnit.Inch => "in",
        _ => "cm",
    };

    protected override void OnRender(DrawingContext dc)
    {
        var length = Vertical ? ActualHeight : ActualWidth;
        var width = Vertical ? ActualWidth : ActualHeight;
        var back = TryFindResource("PanelSurfaceBrush") as Brush ?? Brushes.DimGray;
        var ink = TryFindResource("MutedTextBrush") as Brush ?? Brushes.Gray;
        var accent = TryFindResource("AccentBlueMidBrush") as Brush ?? Brushes.DodgerBlue;
        var text = TryFindResource("ForegroundTextBrush") as Brush ?? Brushes.White;

        var full = Vertical ? new Rect(0, 0, width, length) : new Rect(0, 0, length, width);
        dc.DrawRectangle(back, null, full);
        dc.PushClip(new RectangleGeometry(full));

        if (_document is { } document && length > 0)
        {
            var pen = new Pen(ink, 1);
            pen.Freeze();
            var perUnit = PagePxPerUnit() * _zoom; // screen DIP per unit
            var (step, parts) = Step(perUnit);
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var pages = Vertical ? document.PageCount : 1;
            var extent = Vertical ? document.Height : document.Width;
            for (var page = 0; page < pages; page++)
            {
                var start = _origin + (Vertical ? document.PageTop(page) : 0) * _zoom;
                var end = start + extent * _zoom;
                if (end < 0 || start > length)
                {
                    continue;
                }

                DrawSegment(dc, pen, ink, text, pixelsPerDip, start, extent * _zoom / perUnit, perUnit, step, parts, length, width);

                // the end of the page
                var endLine = Snap(end);
                dc.DrawLine(pen, Point(endLine, 0), Point(endLine, width));
            }

            if (_cursor is { } cursor)
            {
                var at = Snap(_origin + cursor * _zoom);
                if (at >= 0 && at <= length)
                {
                    var markPen = new Pen(accent, 1.5);
                    markPen.Freeze();
                    dc.DrawLine(markPen, Point(at, 0), Point(at, width));
                }
            }
        }

        // the edge towards the work area
        var edgePen = new Pen(ink, 1);
        edgePen.Freeze();
        dc.DrawLine(edgePen, Vertical ? new System.Windows.Point(width - 0.5, 0) : new System.Windows.Point(0, width - 0.5),
            Vertical ? new System.Windows.Point(width - 0.5, length) : new System.Windows.Point(length, width - 0.5));
        dc.Pop();
    }

    private System.Windows.Point Point(double along, double across) => Vertical ? new System.Windows.Point(across, along) : new System.Windows.Point(along, across);

    private static double Snap(double value) => Math.Floor(value) + 0.5;

    /// <summary>The distance between two numbered ticks (1, 2 or 5 x 10^n units: at least 56 DIP apart) and into how many parts it is divided.</summary>
    private static (double Step, int Parts) Step(double screenPerUnit)
    {
        for (var power = -6; power <= 8; power++)
        {
            foreach (var (mantissa, parts) in new[] { (1.0, 10), (2.0, 4), (5.0, 5) })
            {
                var step = mantissa * Math.Pow(10, power);
                if (step * screenPerUnit >= 56)
                {
                    return (step, parts);
                }
            }
        }

        return (1e8, 10);
    }

    private void DrawSegment(
        DrawingContext dc, Pen pen, Brush ink, Brush text, double pixelsPerDip, double start, double lengthInUnits, double perUnit,
        double step, int parts, double rulerLength, double thickness)
    {
        var minor = step / parts;
        var first = Math.Max(0, (int)Math.Floor((0 - start) / (minor * perUnit)));
        var last = Math.Min((int)Math.Floor(lengthInUnits / minor + 1e-9), (int)Math.Ceiling((rulerLength - start) / (minor * perUnit)));
        for (var i = first; i <= last; i++)
        {
            var value = i * minor;
            var at = start + value * perUnit;
            if (at < -1 || at > rulerLength + 1)
            {
                continue;
            }

            var major = i % parts == 0;
            var middle = !major && parts % 2 == 0 && i % (parts / 2) == 0;
            var tick = major ? thickness * 0.62 : middle ? thickness * 0.42 : thickness * 0.24;
            var line = Snap(at);
            dc.DrawLine(pen, Point(line, thickness - tick), Point(line, thickness));

            if (!major)
            {
                continue;
            }

            var label = new FormattedText(
                (Math.Round(value, 6)).ToString("0.######", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 9.5, text, pixelsPerDip);
            if (Vertical)
            {
                // read from the bottom to the top
                dc.PushTransform(new TranslateTransform(2, line + 3 + label.Width));
                dc.PushTransform(new RotateTransform(-90));
                dc.DrawText(label, new System.Windows.Point(0, 0));
                dc.Pop();
                dc.Pop();
            }
            else
            {
                dc.DrawText(label, new System.Windows.Point(line + 3, 1));
            }
        }
    }
}
