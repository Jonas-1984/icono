using System.Globalization;
using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>The measurements of how a dimension is drawn: text size, line width, arrowheads and extension lines.</summary>
public readonly record struct DimensionStyle(
    float FontSize, float LineWidth, float ArrowSize, float ExtGap, float ExtOvershoot,
    string FontFamily, bool Bold, bool Italic, bool Underline)
{
    /// <summary>The style of a dimension shape or of the dimensions of a wall: what was chosen, else the defaults.</summary>
    public static DimensionStyle For(Shape shape)
    {
        var isDimension = shape.Kind == ShapeKind.Dimension;
        var font = (float)Math.Clamp(shape.DimFontSize ?? (isDimension ? shape.TextFontSize : 10.5), 4, 200);
        var line = (float)Math.Clamp(shape.DimLineWidth ?? (isDimension ? shape.BorderThickness : 0.8), 0.1, 30);
        var arrow = (float)Math.Clamp(shape.DimArrowSize ?? Math.Max(6, font * 0.7), 1, 200);
        var gap = (float)Math.Clamp(shape.DimExtGap ?? 3, 0, 200);
        var over = (float)Math.Clamp(shape.DimExtOvershoot ?? 4, 0, 200);
        var family = string.IsNullOrWhiteSpace(shape.TextFontFamily) ? "Segoe UI" : shape.TextFontFamily!;
        return new DimensionStyle(font, line, arrow, gap, over, family, isDimension && shape.TextBold, isDimension && shape.TextItalic, isDimension && shape.TextUnderline);
    }
}

/// <summary>What a dimension looks like: its lines, arrowheads and the written value.</summary>
public sealed class DimensionLayout
{
    public SKPath Lines { get; } = new();

    /// <summary>Arrowheads as (tip, direction the arrow points in).</summary>
    public List<(SKPoint Tip, SKPoint Direction)> Arrows { get; } = [];

    public string Text { get; set; } = string.Empty;

    /// <summary>Centre of the text.</summary>
    public SKPoint TextPosition { get; set; }

    /// <summary>Rotation of the text in degrees.</summary>
    public float TextAngle { get; set; }

    public float TextWidth { get; set; }

    public DimensionStyle Style { get; set; }

    public float FontSize => Style.FontSize;

    public float ArrowLength => Style.ArrowSize;
}

/// <summary>Measuring, laying out and drawing AutoCAD-style dimensions. The value sits in the middle of the dimension line, which is
/// interrupted behind it.</summary>
public static class DimensionRenderer
{
    private const float TextPad = 4f;

    // ----- measuring and writing values ------------------------------------------------------------------

    public static string Format(double px, LengthUnit unit, int precision)
    {
        var inches = px / 96.0;
        var culture = CultureInfo.InvariantCulture;
        var format = "F" + Math.Clamp(precision, 0, 6);
        return unit switch
        {
            LengthUnit.Millimeter => (inches * 25.4).ToString(format, culture) + " mm",
            LengthUnit.Meter => (inches * 0.0254).ToString(format, culture) + " m",
            LengthUnit.Inch => inches.ToString(format, culture) + " in",
            _ => (inches * 2.54).ToString(format, culture) + " cm",
        };
    }

    private static List<SKPoint> Points(Shape dim)
    {
        var list = new List<SKPoint>();
        if (dim.Points is null)
        {
            return list;
        }

        foreach (var p in dim.Points)
        {
            list.Add(new SKPoint((float)(dim.X + p.X), (float)(dim.Y + p.Y)));
        }

        return list;
    }

    /// <summary>Applies the user's own text: "&lt;&gt;" inside it stands for the measured value.</summary>
    private static string Compose(Shape dim, string measured)
    {
        if (string.IsNullOrWhiteSpace(dim.DimText))
        {
            return measured;
        }

        return dim.DimText.Contains("<>") ? dim.DimText.Replace("<>", measured) : dim.DimText;
    }

    private static float MeasureText(string text, DimensionStyle style)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        using var typeface = SKTypeface.FromFamilyName(style.FontFamily,
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, style.FontSize);
        return font.MeasureText(text);
    }

    // ----- small vector helpers --------------------------------------------------------------------------

    private static SKPoint Sub(SKPoint a, SKPoint b) => new(a.X - b.X, a.Y - b.Y);

    private static SKPoint Add(SKPoint a, SKPoint b) => new(a.X + b.X, a.Y + b.Y);

    private static SKPoint Mul(SKPoint a, float s) => new(a.X * s, a.Y * s);

    private static float Len(SKPoint a) => MathF.Sqrt(a.X * a.X + a.Y * a.Y);

    private static SKPoint Unit(SKPoint a)
    {
        var l = Len(a);
        return l < 1e-6f ? new SKPoint(1, 0) : new SKPoint(a.X / l, a.Y / l);
    }

    /// <summary>A text angle that reads left to right (never upside down).</summary>
    private static float Readable(float degrees)
    {
        var angle = degrees;
        while (angle > 90f)
        {
            angle -= 180f;
        }

        while (angle <= -90f)
        {
            angle += 180f;
        }

        return angle;
    }

    // ----- layout ----------------------------------------------------------------------------------------

    public static DimensionLayout Layout(Shape dim)
    {
        var layout = new DimensionLayout { Style = DimensionStyle.For(dim) };

        var pts = Points(dim);
        if (pts.Count < 2)
        {
            return layout;
        }

        switch (dim.DimKind)
        {
            case DimensionKind.Horizontal:
            case DimensionKind.Vertical:
            case DimensionKind.Aligned:
                LayoutLinear(dim, pts, layout);
                break;
            case DimensionKind.Angular:
                LayoutAngular(dim, pts, layout);
                break;
            case DimensionKind.Radius:
            case DimensionKind.Diameter:
                LayoutRound(dim, pts, layout);
                break;
            case DimensionKind.Length:
                LayoutLength(dim, pts, layout);
                break;
        }

        return layout;
    }

    /// <summary>A dimension along two points, its line <paramref name="offset"/> away to the left of the direction p1 to p2 (used
    /// for the lengths written next to walls).</summary>
    public static DimensionLayout LayoutAligned(SKPoint p1, SKPoint p2, float offset, DimensionStyle style, string text)
    {
        var layout = new DimensionLayout { Style = style };
        var u = Unit(Sub(p2, p1));
        var n = new SKPoint(-u.Y, u.X);
        var a = Add(p1, Mul(n, offset));
        var b = Add(p2, Mul(n, offset));
        ExtensionLine(layout, p1, a);
        ExtensionLine(layout, p2, b);
        layout.Text = text;
        PutTextOnLine(layout, a, b, MathF.Atan2(u.Y, u.X) * 180f / MathF.PI);
        return layout;
    }

    private static void ExtensionLine(DimensionLayout layout, SKPoint from, SKPoint to)
    {
        var d = Sub(to, from);
        if (Len(d) < 0.5f)
        {
            return;
        }

        var u = Unit(d);
        var start = Add(from, Mul(u, layout.Style.ExtGap));
        var end = Add(to, Mul(u, layout.Style.ExtOvershoot));
        if (Len(Sub(end, from)) <= layout.Style.ExtGap)
        {
            return;
        }

        layout.Lines.MoveTo(start);
        layout.Lines.LineTo(end);
    }

    /// <summary>The dimension line between a and b with an arrowhead at each end, and the value in the middle of it with the line broken
    /// behind the number. When the number does not fit between the arrows it goes above the line instead.</summary>
    private static void PutTextOnLine(DimensionLayout layout, SKPoint a, SKPoint b, float lineAngleDegrees)
    {
        var d = Sub(b, a);
        var length = Len(d);
        if (length < 0.5f)
        {
            return;
        }

        var u = Unit(d);
        var arrow = layout.ArrowLength;
        layout.TextWidth = MeasureText(layout.Text, layout.Style);
        layout.TextAngle = Readable(lineAngleDegrees);
        var middle = new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var half = layout.TextWidth / 2 + TextPad;
        var fitsInLine = layout.Text.Length == 0 || (length - 2 * half) / 2 >= arrow * 1.2f;

        if (length >= arrow * 2.4f)
        {
            if (layout.Text.Length > 0 && fitsInLine)
            {
                // two pieces of line, the number sits in the gap
                layout.Lines.MoveTo(a);
                layout.Lines.LineTo(Add(middle, Mul(u, -half)));
                layout.Lines.MoveTo(Add(middle, Mul(u, half)));
                layout.Lines.LineTo(b);
                layout.TextPosition = middle;
            }
            else
            {
                layout.Lines.MoveTo(a);
                layout.Lines.LineTo(b);
                layout.TextPosition = AboveLine(layout, middle);
            }

            layout.Arrows.Add((a, Mul(u, -1)));
            layout.Arrows.Add((b, u));
        }
        else
        {
            // no room inside: the arrows point in from outside and the line runs on past the ends
            var tail = arrow + 4f;
            layout.Lines.MoveTo(Add(a, Mul(u, -tail)));
            layout.Lines.LineTo(Add(b, Mul(u, tail)));
            layout.Arrows.Add((a, u));
            layout.Arrows.Add((b, Mul(u, -1)));
            layout.TextPosition = AboveLine(layout, middle);
        }
    }

    private static SKPoint AboveLine(DimensionLayout layout, SKPoint middle)
    {
        var radians = layout.TextAngle * MathF.PI / 180f;
        var up = new SKPoint(MathF.Sin(radians), -MathF.Cos(radians));
        return Add(middle, Mul(up, layout.FontSize * 0.7f + 2f + layout.Style.LineWidth));
    }

    private static void LayoutLinear(Shape dim, List<SKPoint> pts, DimensionLayout layout)
    {
        var p1 = pts[0];
        var p2 = pts[1];
        var p3 = pts.Count > 2 ? pts[2] : (SKPoint?)null;

        SKPoint a;
        SKPoint b;
        double value;
        float angle;

        if (dim.DimKind == DimensionKind.Horizontal)
        {
            var y = p3?.Y ?? p1.Y;
            a = new SKPoint(p1.X, y);
            b = new SKPoint(p2.X, y);
            value = Math.Abs(p2.X - p1.X);
            angle = 0;
        }
        else if (dim.DimKind == DimensionKind.Vertical)
        {
            var x = p3?.X ?? p1.X;
            a = new SKPoint(x, p1.Y);
            b = new SKPoint(x, p2.Y);
            value = Math.Abs(p2.Y - p1.Y);
            angle = -90;
        }
        else
        {
            var u = Unit(Sub(p2, p1));
            var n = new SKPoint(-u.Y, u.X);
            var offset = p3 is { } c ? (c.X - p1.X) * n.X + (c.Y - p1.Y) * n.Y : 0f;
            a = Add(p1, Mul(n, offset));
            b = Add(p2, Mul(n, offset));
            value = Len(Sub(p2, p1));
            angle = MathF.Atan2(u.Y, u.X) * 180f / MathF.PI;
        }

        ExtensionLine(layout, p1, a);
        ExtensionLine(layout, p2, b);

        if (dim.DimRole == DimensionRole.Depth && dim.DimValue > 0)
        {
            value = dim.DimValue;
        }

        layout.Text = Compose(dim, Format(value, dim.DimUnit, dim.DimPrecision));
        PutTextOnLine(layout, a, b, angle);
    }

    private static void LayoutAngular(Shape dim, List<SKPoint> pts, DimensionLayout layout)
    {
        var v = pts[0];
        var pa = pts[1];
        var pb = pts.Count > 2 ? pts[2] : pts[1];
        var da = Sub(pa, v);
        var db = Sub(pb, v);
        if (Len(da) < 0.5f)
        {
            return;
        }

        var gap = layout.Style.ExtGap;
        layout.Lines.MoveTo(Add(v, Mul(Unit(da), gap)));
        layout.Lines.LineTo(pa);

        if (pts.Count < 3 || Len(db) < 0.5f)
        {
            return;
        }

        layout.Lines.MoveTo(Add(v, Mul(Unit(db), gap)));
        layout.Lines.LineTo(pb);

        var radius = pts.Count > 3 ? Len(Sub(pts[3], v)) : 0.6f * MathF.Min(Len(da), Len(db));
        radius = MathF.Max(radius, 14f);

        var alpha = MathF.Atan2(da.Y, da.X);
        var beta = MathF.Atan2(db.Y, db.X);
        var sweep = beta - alpha;
        while (sweep > MathF.PI)
        {
            sweep -= 2 * MathF.PI;
        }

        while (sweep <= -MathF.PI)
        {
            sweep += 2 * MathF.PI;
        }

        var sign = sweep >= 0 ? 1f : -1f;
        var rect = new SKRect(v.X - radius, v.Y - radius, v.X + radius, v.Y + radius);
        var startPoint = new SKPoint(v.X + radius * MathF.Cos(alpha), v.Y + radius * MathF.Sin(alpha));
        var endPoint = new SKPoint(v.X + radius * MathF.Cos(beta), v.Y + radius * MathF.Sin(beta));
        layout.Arrows.Add((startPoint, new SKPoint(sign * MathF.Sin(alpha), -sign * MathF.Cos(alpha))));
        layout.Arrows.Add((endPoint, new SKPoint(-sign * MathF.Sin(beta), sign * MathF.Cos(beta))));

        var degrees = MathF.Abs(sweep) * 180f / MathF.PI;
        layout.Text = Compose(dim, degrees.ToString("F" + Math.Clamp(dim.DimPrecision, 0, 6), CultureInfo.InvariantCulture) + "°");
        layout.TextWidth = MeasureText(layout.Text, layout.Style);

        var gamma = alpha + sweep / 2;
        var halfAngle = (layout.TextWidth / 2 + TextPad) / radius;
        var arrowAngle = layout.ArrowLength * 1.2f / radius;
        var startDeg = alpha * 180f / MathF.PI;

        if (MathF.Abs(sweep) / 2 - halfAngle > arrowAngle)
        {
            // arc in two pieces with the number in the gap, turned along the arc
            var piece = (MathF.Abs(sweep) / 2 - halfAngle) * 180f / MathF.PI * sign;
            layout.Lines.AddArc(rect, startDeg, piece);
            layout.Lines.AddArc(rect, (gamma + sign * halfAngle) * 180f / MathF.PI, piece);
            layout.TextAngle = Readable(gamma * 180f / MathF.PI + 90f);
            layout.TextPosition = new SKPoint(v.X + radius * MathF.Cos(gamma), v.Y + radius * MathF.Sin(gamma));
        }
        else
        {
            layout.Lines.AddArc(rect, startDeg, sweep * 180f / MathF.PI);
            layout.TextAngle = 0;
            layout.TextPosition = new SKPoint(
                v.X + (radius + layout.FontSize * 1.1f + layout.TextWidth * 0.3f) * MathF.Cos(gamma),
                v.Y + (radius + layout.FontSize * 1.1f + layout.TextWidth * 0.3f) * MathF.Sin(gamma));
        }
    }

    private static void LayoutRound(Shape dim, List<SKPoint> pts, DimensionLayout layout)
    {
        var center = pts[0];
        var edge = pts[1];
        var radius = Len(Sub(edge, center));
        if (radius < 0.5f)
        {
            return;
        }

        var u = Unit(Sub(edge, center));
        var angle = MathF.Atan2(u.Y, u.X) * 180f / MathF.PI;

        if (dim.DimKind == DimensionKind.Diameter)
        {
            var far = Sub(center, Mul(u, radius));
            layout.Text = Compose(dim, "Ø " + Format(radius * 2, dim.DimUnit, dim.DimPrecision));
            PutTextOnLine(layout, far, edge, angle);
        }
        else
        {
            // the arrow points at the circle, so run the line from the centre out to it
            layout.Text = Compose(dim, "R " + Format(radius, dim.DimUnit, dim.DimPrecision));
            PutTextOnLine(layout, center, edge, angle);

            // only the arrow at the circle is wanted, not one at the centre
            layout.Arrows.RemoveAll(arrow => arrow.Tip.X == center.X && arrow.Tip.Y == center.Y);
        }

        // small centre mark
        layout.Lines.MoveTo(center.X - 3.5f, center.Y);
        layout.Lines.LineTo(center.X + 3.5f, center.Y);
        layout.Lines.MoveTo(center.X, center.Y - 3.5f);
        layout.Lines.LineTo(center.X, center.Y + 3.5f);
    }

    private static void LayoutLength(Shape dim, List<SKPoint> pts, DimensionLayout layout)
    {
        var anchor = pts[0];
        var textAt = pts[1];
        var d = Sub(textAt, anchor);
        if (Len(d) > 0.5f)
        {
            layout.Lines.MoveTo(anchor);
            layout.Lines.LineTo(textAt);
            layout.Arrows.Add((anchor, Mul(Unit(d), -1)));
        }

        layout.Text = Compose(dim, "L " + Format(dim.DimValue, dim.DimUnit, dim.DimPrecision));
        layout.TextWidth = MeasureText(layout.Text, layout.Style);
        layout.TextAngle = 0;
        layout.TextPosition = Add(textAt, new SKPoint(0, -layout.FontSize * 0.7f - 2f));
    }

    // ----- drawing --------------------------------------------------------------------------------------

    public static void Draw(SKCanvas canvas, Shape dim, byte opacityAlpha)
    {
        var layout = Layout(dim);
        var color = dim.BorderColor.ToSkColor();
        DrawLayout(canvas, layout, color.WithAlpha((byte)(color.Alpha * opacityAlpha / 255.0)));
    }

    public static void DrawLayout(SKCanvas canvas, DimensionLayout layout, SKColor color)
    {
        if (layout.Lines.IsEmpty && layout.Text.Length == 0)
        {
            return;
        }

        var style = layout.Style;
        using var linePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = style.LineWidth, Color = color, StrokeCap = SKStrokeCap.Butt };
        canvas.DrawPath(layout.Lines, linePaint);

        using var arrowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };
        foreach (var (tip, direction) in layout.Arrows)
        {
            using var head = new SKPath();
            var back = new SKPoint(tip.X - direction.X * layout.ArrowLength, tip.Y - direction.Y * layout.ArrowLength);
            var side = new SKPoint(-direction.Y * layout.ArrowLength * 0.28f, direction.X * layout.ArrowLength * 0.28f);
            head.MoveTo(tip);
            head.LineTo(back.X + side.X, back.Y + side.Y);
            head.LineTo(back.X - side.X, back.Y - side.Y);
            head.Close();
            canvas.DrawPath(head, arrowPaint);
        }

        if (layout.Text.Length == 0)
        {
            return;
        }

        using var typeface = SKTypeface.FromFamilyName(style.FontFamily,
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, style.FontSize);
        var textWidth = font.MeasureText(layout.Text);

        canvas.Save();
        canvas.Translate(layout.TextPosition.X, layout.TextPosition.Y);
        canvas.RotateDegrees(layout.TextAngle);

        // A pale halo keeps the number readable where something else of the drawing runs behind it.
        using var halo = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(255, 255, 255, 170),
        };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };
        var baseline = style.FontSize * 0.35f;
        using var textPath = font.GetTextPath(layout.Text, new SKPoint(-textWidth / 2, baseline));
        canvas.DrawPath(textPath, halo);
        canvas.DrawPath(textPath, fill);
        if (style.Underline)
        {
            canvas.DrawLine(-textWidth / 2, baseline + 2, textWidth / 2, baseline + 2, linePaint);
        }

        canvas.Restore();
    }

    /// <summary>The drawn lines of the dimension as a path (used as its geometry).</summary>
    public static SKPath BuildPath(Shape dim)
    {
        var layout = Layout(dim);
        return new SKPath(layout.Lines);
    }

    /// <summary>True when (x,y) is on a line, arrowhead or the written value of the dimension.</summary>
    public static bool HitTest(Shape dim, double x, double y, double tolerance)
    {
        var layout = Layout(dim);
        if (!layout.Lines.IsEmpty)
        {
            using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)(tolerance * 2) };
            using var band = new SKPath();
            stroke.GetFillPath(layout.Lines, band);
            if (band.Contains((float)x, (float)y))
            {
                return true;
            }
        }

        foreach (var (tip, _) in layout.Arrows)
        {
            if (Math.Abs(tip.X - x) <= layout.ArrowLength && Math.Abs(tip.Y - y) <= layout.ArrowLength)
            {
                return true;
            }
        }

        if (layout.Text.Length == 0)
        {
            return false;
        }

        var half = layout.TextWidth / 2 + (float)tolerance;
        var radians = -layout.TextAngle * MathF.PI / 180f;
        var dx = x - layout.TextPosition.X;
        var dy = y - layout.TextPosition.Y;
        var localX = dx * Math.Cos(radians) - dy * Math.Sin(radians);
        var localY = dx * Math.Sin(radians) + dy * Math.Cos(radians);
        return Math.Abs(localX) <= half && Math.Abs(localY) <= layout.FontSize * 0.7 + tolerance;
    }
}
