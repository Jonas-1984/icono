using Avalonia;
using Icono.Core;
using SkiaSharp;

namespace Icono.Mobile.Views;

/// <summary>The rulers along the top and the left edge of the work area, as in the Windows program: real-world lengths (page length x drawing scale) in
/// the unit of the canvas (mm, cm, inch), zero at the corner of every page, marks that follow the finger. Drawn over the canvas by the same renderer.</summary>
internal static class RulerPainter
{
    /// <summary>Width of the left ruler, height of the top one (DIP).</summary>
    public const float Thickness = 22;

    private static readonly SKColor Back = new(0x14, 0x17, 0x1E, 0xF0);
    private static readonly SKColor Ink = new(0x8A, 0x94, 0xA6);
    private static readonly SKColor Text = new(0xE9, 0xF0, 0xFA);
    private static readonly SKColor Accent = new(0x5A, 0xB0, 0xFF);

    public static string UnitLabel(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => "mm",
        LengthUnit.Meter => "m",
        LengthUnit.Inch => "in",
        _ => "cm",
    };

    /// <summary>Page pixels for one unit of real length at the drawing scale (a centimetre is 96 / 2.54 px on a 1:1 page).</summary>
    private static double PagePxPerUnit(LengthUnit unit, double scale)
    {
        var perUnit = unit switch
        {
            LengthUnit.Millimeter => 96.0 / 25.4,
            LengthUnit.Meter => 96.0 / 0.0254,
            LengthUnit.Inch => 96.0,
            _ => 96.0 / 2.54,
        };
        return perUnit / (scale > 0 ? scale : 1);
    }

    /// <summary>The distance between two marked numbers (in units) and how many small marks lie in between, so that the numbers are about 60 DIP apart.</summary>
    private static (double Step, int Parts) Step(double screenPerUnit)
    {
        var raw = 60 / Math.Max(1e-9, screenPerUnit);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normal = raw / magnitude;
        return normal switch
        {
            <= 1 => (magnitude, 10),
            <= 2 => (2 * magnitude, 4),
            <= 5 => (5 * magnitude, 5),
            _ => (10 * magnitude, 10),
        };
    }

    public static void Draw(
        SKCanvas canvas, float width, float height, Document document, double zoom, Vector offset, LengthUnit unit, Point? cursor)
    {
        var screenPerUnit = PagePxPerUnit(unit, document.ScaleDenominator) * zoom;
        var (step, parts) = Step(screenPerUnit);
        var minor = step / parts;

        using var back = new SKPaint { Style = SKPaintStyle.Fill, Color = Back };
        using var tick = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = Ink };
        using var edge = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(0x3A, 0x44, 0x57) };
        using var font = new SKFont(SKTypeface.Default, 9);
        using var text = new SKPaint { IsAntialias = true, Color = Text };
        using var mark = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = Accent };

        canvas.DrawRect(0, 0, width, Thickness, back);
        canvas.DrawRect(0, 0, Thickness, height, back);
        canvas.DrawLine(Thickness, Thickness - 0.5f, width, Thickness - 0.5f, edge);
        canvas.DrawLine(Thickness - 0.5f, Thickness, Thickness - 0.5f, height, edge);

        // along the top: the width of the page (it is the same for every page)
        Marks(canvas, tick, text, font, horizontal: true, start: offset.X, extentPx: document.Width * zoom, lengthLimit: width, screenPerUnit, step, parts, minor);

        // down the left: every page starts at zero again
        for (var page = 0; page < document.PageCount; page++)
        {
            Marks(canvas, tick, text, font, horizontal: false, start: offset.Y + document.PageTop(page) * zoom, extentPx: document.Height * zoom, lengthLimit: height,
                screenPerUnit, step, parts, minor);
        }

        // the corner shows the unit; the marks of the finger show where it is
        canvas.DrawRect(0, 0, Thickness, Thickness, back);
        font.Size = 10;
        canvas.DrawText(UnitLabel(unit), 4, 15, font, text);
        if (cursor is { } finger)
        {
            if (finger.X > Thickness)
            {
                canvas.DrawLine((float)finger.X, 0, (float)finger.X, Thickness, mark);
            }

            if (finger.Y > Thickness)
            {
                canvas.DrawLine(0, (float)finger.Y, Thickness, (float)finger.Y, mark);
            }
        }
    }

    private static void Marks(
        SKCanvas canvas, SKPaint tick, SKPaint text, SKFont font, bool horizontal, double start, double extentPx, float lengthLimit, double screenPerUnit,
        double step, int parts, double minor)
    {
        var end = start + extentPx;
        if (end < Thickness || start > lengthLimit)
        {
            return;
        }

        var units = extentPx / screenPerUnit;
        var first = Math.Max(0, (int)Math.Floor((Thickness - start) / screenPerUnit / minor));
        var last = (int)Math.Min(Math.Ceiling(units / minor), Math.Ceiling((lengthLimit - start) / screenPerUnit / minor));
        font.Size = 9;
        for (var k = first; k <= last; k++)
        {
            var value = k * minor;
            if (value > units + minor * 0.01)
            {
                break;
            }

            var at = (float)Math.Round(start + value * screenPerUnit) + 0.5f;
            if (at < Thickness || at > lengthLimit)
            {
                continue;
            }

            var isMajor = k % parts == 0;
            var isHalf = parts % 2 == 0 && k % parts == parts / 2;
            var size = isMajor ? Thickness - 5 : isHalf ? 9 : 5;
            if (horizontal)
            {
                canvas.DrawLine(at, Thickness - size, at, Thickness - 1, tick);
            }
            else
            {
                canvas.DrawLine(Thickness - size, at, Thickness - 1, at, tick);
            }

            if (!isMajor)
            {
                continue;
            }

            var label = Number(value);
            if (horizontal)
            {
                canvas.DrawText(label, at + 3, 10, font, text);
            }
            else
            {
                canvas.Save();
                canvas.Translate(10, at + 3);
                canvas.RotateDegrees(-90);
                canvas.DrawText(label, 0, 0, font, text);
                canvas.Restore();
            }
        }

        // the end of the page
        var closing = (float)Math.Round(end) + 0.5f;
        if (closing > Thickness && closing < lengthLimit)
        {
            if (horizontal)
            {
                canvas.DrawLine(closing, 0, closing, Thickness, tick);
            }
            else
            {
                canvas.DrawLine(0, closing, Thickness, closing, tick);
            }
        }
    }

    private static string Number(double value) =>
        value.ToString(Math.Abs(value) >= 100 || Math.Abs(value - Math.Round(value)) < 1e-9 ? "0" : Math.Abs(value) >= 10 ? "0.#" : "0.##", System.Globalization.CultureInfo.InvariantCulture);
}
