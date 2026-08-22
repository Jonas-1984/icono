using System.Globalization;
using System.Text.RegularExpressions;
using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// Turns pasted SVG markup (or bare path "d" data) into one or more <see cref="ShapeKind.CombinedPath"/>
/// shapes. When the markup has several elements (e.g. a background shape plus a foreground glyph),
/// every element is normalized against ONE SHARED bounding box instead of its own — so splitting
/// the icon into independently colorable shapes doesn't destroy how they line up.
/// </summary>
public static class SvgImporter
{
    private static readonly Regex ElementRegex = new(
        """<(path|circle|ellipse|rect)\b([^>]*)/?>""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AttributeRegex = new(
        """([\w:-]+)\s*=\s*["']([^"']*)["']""", RegexOptions.Compiled);

    private static readonly Regex FillNoneRegex = new(
        """fill\s*=\s*["']none["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when the markup declares <c>fill="none"</c> anywhere (the Lucide/Feather/Tabler
    /// convention for stroke-only icons) — used to default an import to a stroked outline instead
    /// of a solid fill, which is what almost every hand-pasted icon library actually wants.</summary>
    public static bool IsStrokeStyleSvg(string svgMarkup) => FillNoneRegex.IsMatch(svgMarkup);

    /// <summary>One drawable element parsed out of pasted markup: its path data plus whatever fill
    /// color IT SPECIFICALLY declared (<c>fill="..."</c> or a <c>style="fill:...`</c>), if any. Null
    /// means the element didn't say — <see cref="BuildShapes(IReadOnlyList{SvgElement},ArgbColor,ArgbColor,double,double,double,double,double,bool)"/>
    /// falls back to its caller-supplied default color for those, which is what keeps a
    /// single-color Lucide/Feather/Tabler icon (no per-element fill at all) working exactly as
    /// before this was added.</summary>
    public readonly record struct SvgElement(string PathData, ArgbColor? Fill);

    /// <summary>Extracts one path-data ("d" attribute) string per &lt;path&gt;, &lt;circle&gt;,
    /// &lt;ellipse&gt;, and &lt;rect&gt; element found in <paramref name="svgMarkup"/>, in document
    /// order — the non-&lt;path&gt; primitives are converted to equivalent "d" data (a circle/ellipse
    /// as two half-arcs, a rect as four sides, rounded corners as arcs) since <see cref="BuildShapes(IReadOnlyList{string},ArgbColor,ArgbColor,double,double,double,double,double,bool)"/>
    /// only knows how to parse path data. Falls back to treating the whole trimmed input as one bare
    /// path's data if no matching elements are found (so pasting just a "d" value works too). Plain
    /// path data only — use <see cref="ExtractElements"/> to also capture each element's own fill
    /// color for a multi-color source (e.g. a brand mark with two differently-colored pieces).</summary>
    public static IReadOnlyList<string> ExtractPathData(string svgMarkup) =>
        ExtractElements(svgMarkup).Select(e => e.PathData).ToList();

    /// <summary>Same element extraction as <see cref="ExtractPathData"/>, but also captures each
    /// element's own <c>fill</c> — needed for a source SVG that colors different pieces
    /// differently (a two-tone logo, say) instead of the single-color stroke-icon convention
    /// <see cref="IsStrokeStyleSvg"/> targets.</summary>
    public static IReadOnlyList<SvgElement> ExtractElements(string svgMarkup)
    {
        var matches = ElementRegex.Matches(svgMarkup);
        if (matches.Count == 0)
        {
            var trimmed = svgMarkup.Trim();
            return trimmed.Length > 0 ? [new SvgElement(trimmed, null)] : [];
        }

        var result = new List<SvgElement>();
        foreach (Match match in matches)
        {
            var tag = match.Groups[1].Value.ToLowerInvariant();
            var attrs = ParseAttributes(match.Groups[2].Value);

            var d = tag switch
            {
                "path" => attrs.GetValueOrDefault("d"),
                "circle" => BuildCirclePathData(attrs),
                "ellipse" => BuildEllipsePathData(attrs),
                "rect" => BuildRectPathData(attrs),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(d))
            {
                result.Add(new SvgElement(d, ParseFillColor(attrs)));
            }
        }

        return result;
    }

    /// <summary>Reads this element's own <c>fill="..."</c> (or <c>style="fill:...;"</c>) attribute
    /// and parses it as a color. Null for "not specified", <c>none</c>/<c>currentColor</c>/<c>inherit</c>/
    /// <c>transparent</c> (none of which name an actual color this element owns) — all of which mean
    /// "the caller's default color applies here instead".</summary>
    private static ArgbColor? ParseFillColor(Dictionary<string, string> attrs)
    {
        var raw = attrs.GetValueOrDefault("fill");
        if (string.IsNullOrWhiteSpace(raw) && attrs.TryGetValue("style", out var style))
        {
            var styleMatch = StyleFillRegex.Match(style);
            if (styleMatch.Success)
            {
                raw = styleMatch.Groups[1].Value.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        return raw is "none" or "currentcolor" or "inherit" or "transparent"
            ? null
            : TryParseCssColor(raw, out var color) ? color : null;
    }

    private static readonly Regex StyleFillRegex = new(
        """fill\s*:\s*([^;]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HexColorRegex = new(
        "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    private static readonly Regex RgbColorRegex = new(
        """^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, ArgbColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = ArgbColor.FromRgb(0, 0, 0),
        ["white"] = ArgbColor.FromRgb(255, 255, 255),
        ["red"] = ArgbColor.FromRgb(255, 0, 0),
        ["green"] = ArgbColor.FromRgb(0, 128, 0),
        ["blue"] = ArgbColor.FromRgb(0, 0, 255),
        ["gray"] = ArgbColor.FromRgb(128, 128, 128),
        ["grey"] = ArgbColor.FromRgb(128, 128, 128),
        ["orange"] = ArgbColor.FromRgb(255, 165, 0),
        ["yellow"] = ArgbColor.FromRgb(255, 255, 0),
        ["purple"] = ArgbColor.FromRgb(128, 0, 128),
        ["pink"] = ArgbColor.FromRgb(255, 192, 203),
        ["brown"] = ArgbColor.FromRgb(165, 42, 42),
        ["cyan"] = ArgbColor.FromRgb(0, 255, 255),
        ["magenta"] = ArgbColor.FromRgb(255, 0, 255),
        ["navy"] = ArgbColor.FromRgb(0, 0, 128),
        ["teal"] = ArgbColor.FromRgb(0, 128, 128),
        ["lime"] = ArgbColor.FromRgb(0, 255, 0),
        ["indigo"] = ArgbColor.FromRgb(75, 0, 130),
        ["gold"] = ArgbColor.FromRgb(255, 215, 0),
        ["silver"] = ArgbColor.FromRgb(192, 192, 192),
        ["maroon"] = ArgbColor.FromRgb(128, 0, 0),
        ["olive"] = ArgbColor.FromRgb(128, 128, 0),
        ["skyblue"] = ArgbColor.FromRgb(135, 206, 235),
    };

    /// <summary>Parses a CSS color in the small subset a hand-pasted SVG actually tends to use:
    /// <c>#rgb</c>/<c>#rgba</c>/<c>#rrggbb</c>/<c>#rrggbbaa</c> hex, <c>rgb()</c>/<c>rgba()</c>
    /// functions, and a modest set of common named colors. Not a full CSS color parser (no
    /// <c>hsl()</c>, no the-rest-of-the-147-named-colors) — good enough for real icon markup
    /// without pulling in a WPF/CSS color dependency this rendering-only project doesn't have.</summary>
    private static bool TryParseCssColor(string raw, out ArgbColor color)
    {
        var hexMatch = HexColorRegex.Match(raw);
        if (hexMatch.Success)
        {
            var hex = hexMatch.Groups[1].Value;
            color = hex.Length switch
            {
                3 => ArgbColor.FromRgb(
                    Convert.ToByte(new string(hex[0], 2), 16),
                    Convert.ToByte(new string(hex[1], 2), 16),
                    Convert.ToByte(new string(hex[2], 2), 16)),
                4 => new ArgbColor(
                    Convert.ToByte(new string(hex[3], 2), 16),
                    Convert.ToByte(new string(hex[0], 2), 16),
                    Convert.ToByte(new string(hex[1], 2), 16),
                    Convert.ToByte(new string(hex[2], 2), 16)),
                6 => ArgbColor.FromRgb(
                    Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)),
                8 => new ArgbColor(
                    Convert.ToByte(hex[6..8], 16),
                    Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)),
                _ => ArgbColor.FromRgb(0, 0, 0),
            };
            return true;
        }

        var rgbMatch = RgbColorRegex.Match(raw);
        if (rgbMatch.Success)
        {
            var r = (byte)Math.Clamp(double.Parse(rgbMatch.Groups[1].Value, CultureInfo.InvariantCulture), 0, 255);
            var g = (byte)Math.Clamp(double.Parse(rgbMatch.Groups[2].Value, CultureInfo.InvariantCulture), 0, 255);
            var b = (byte)Math.Clamp(double.Parse(rgbMatch.Groups[3].Value, CultureInfo.InvariantCulture), 0, 255);
            var a = rgbMatch.Groups[4].Success
                ? (byte)Math.Clamp(double.Parse(rgbMatch.Groups[4].Value, CultureInfo.InvariantCulture) * 255, 0, 255)
                : (byte)255;
            color = new ArgbColor(a, r, g, b);
            return true;
        }

        if (NamedColors.TryGetValue(raw, out color))
        {
            return true;
        }

        color = default;
        return false;
    }

    private static Dictionary<string, string> ParseAttributes(string attributeText)
    {
        var result = new Dictionary<string, string>();
        foreach (Match m in AttributeRegex.Matches(attributeText))
        {
            result[m.Groups[1].Value] = m.Groups[2].Value;
        }

        return result;
    }

    private static double GetAttr(Dictionary<string, string> attrs, string name, double fallback = 0) =>
        attrs.TryGetValue(name, out var raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static string? BuildCirclePathData(Dictionary<string, string> attrs)
    {
        var cx = GetAttr(attrs, "cx");
        var cy = GetAttr(attrs, "cy");
        var r = GetAttr(attrs, "r");
        if (r <= 0)
        {
            return null;
        }

        return FormattableString.Invariant(
            $"M{cx - r},{cy} A{r},{r} 0 1 0 {cx + r},{cy} A{r},{r} 0 1 0 {cx - r},{cy} Z");
    }

    private static string? BuildEllipsePathData(Dictionary<string, string> attrs)
    {
        var cx = GetAttr(attrs, "cx");
        var cy = GetAttr(attrs, "cy");
        var rx = GetAttr(attrs, "rx");
        var ry = GetAttr(attrs, "ry");
        if (rx <= 0 || ry <= 0)
        {
            return null;
        }

        return FormattableString.Invariant(
            $"M{cx - rx},{cy} A{rx},{ry} 0 1 0 {cx + rx},{cy} A{rx},{ry} 0 1 0 {cx - rx},{cy} Z");
    }

    private static string? BuildRectPathData(Dictionary<string, string> attrs)
    {
        var x = GetAttr(attrs, "x");
        var y = GetAttr(attrs, "y");
        var width = GetAttr(attrs, "width");
        var height = GetAttr(attrs, "height");
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var rx = GetAttr(attrs, "rx", -1);
        var ry = GetAttr(attrs, "ry", -1);
        if (rx < 0 && ry < 0)
        {
            return FormattableString.Invariant($"M{x},{y} H{x + width} V{y + height} H{x} Z");
        }

        if (rx < 0)
        {
            rx = ry;
        }

        if (ry < 0)
        {
            ry = rx;
        }

        rx = Math.Min(rx, width / 2);
        ry = Math.Min(ry, height / 2);

        return FormattableString.Invariant(
            $"M{x + rx},{y} H{x + width - rx} A{rx},{ry} 0 0 1 {x + width},{y + ry} V{y + height - ry} A{rx},{ry} 0 0 1 {x + width - rx},{y + height} H{x + rx} A{rx},{ry} 0 0 1 {x},{y + height - ry} V{y + ry} A{rx},{ry} 0 0 1 {x + rx},{y} Z");
    }

    /// <summary>Computes the union bounding box across every path in <paramref name="pathDataStrings"/>
    /// — the SAME box <see cref="BuildShapes"/> normalizes against internally — so a caller can size
    /// its placement box to the source's own aspect ratio BEFORE calling <see cref="BuildShapes"/>,
    /// instead of forcing an arbitrary fixed box. <see cref="BuildShapes"/> stretches its normalized
    /// 24x24 geometry non-uniformly to fill whatever Width/Height it's given — that's a deliberate,
    /// caller-controlled feature (e.g. <c>ShapeCombiner</c> intentionally reuses the source's own
    /// bounds so nothing stretches at all) — but a caller that instead wants an UNDISTORTED import
    /// must pass a Width/Height in the same proportion as this box, or the shape renders squashed
    /// or stretched to whatever fixed size the caller happened to pick. Null if nothing parses.</summary>
    public static (double Width, double Height)? MeasureBounds(IReadOnlyList<string> pathDataStrings)
    {
        SKRect? bounds = null;

        foreach (var d in pathDataStrings)
        {
            using var path = SKPath.ParseSvgPathData(d);
            if (path is null)
            {
                continue;
            }

            bounds = bounds is { } existing ? SKRect.Union(existing, path.Bounds) : path.Bounds;
        }

        return bounds is { Width: > 0, Height: > 0 } b ? (b.Width, b.Height) : null;
    }

    /// <summary>Parses each path's data, computes one shared bounding box across all of them, and
    /// returns a CombinedPath <see cref="Shape"/> per path — all sharing the same placement
    /// (X/Y/Width/Height), each normalized into that shared 24x24 space (mirroring
    /// <see cref="ShapeCombiner"/>'s convention) so their relative position/scale survives the
    /// split. Every shape gets <paramref name="fillColor"/> — use the
    /// <see cref="BuildShapes(IReadOnlyList{SvgElement},ArgbColor,ArgbColor,double,double,double,double,double,bool)"/>
    /// overload for a multi-color source where each piece should keep its OWN color instead.
    /// Returns an empty list if nothing in <paramref name="pathDataStrings"/> parses to a
    /// non-degenerate path.</summary>
    public static IReadOnlyList<Shape> BuildShapes(
        IReadOnlyList<string> pathDataStrings, ArgbColor fillColor, ArgbColor borderColor,
        double borderThickness, double placeX, double placeY, double placeWidth, double placeHeight,
        bool strokeOnly = false) =>
        BuildShapes(
            pathDataStrings.Select(d => new SvgElement(d, null)).ToList(),
            fillColor, borderColor, borderThickness, placeX, placeY, placeWidth, placeHeight, strokeOnly);

    /// <summary>Same as the plain-strings overload, but each element that specified its OWN fill
    /// color (see <see cref="ExtractElements"/>) keeps that color instead of getting
    /// <paramref name="defaultFillColor"/> — e.g. a two-tone logo's darker and lighter pieces
    /// import as two differently-colored shapes instead of being flattened to one uniform color.</summary>
    public static IReadOnlyList<Shape> BuildShapes(
        IReadOnlyList<SvgElement> elements, ArgbColor defaultFillColor, ArgbColor borderColor,
        double borderThickness, double placeX, double placeY, double placeWidth, double placeHeight,
        bool strokeOnly = false)
    {
        var parsed = new List<(SKPath Path, ArgbColor? Fill)>();

        try
        {
            foreach (var element in elements)
            {
                var path = SKPath.ParseSvgPathData(element.PathData);
                if (path is not null)
                {
                    parsed.Add((path, element.Fill));
                }
            }

            if (parsed.Count == 0)
            {
                return [];
            }

            var bounds = parsed[0].Path.Bounds;
            for (var i = 1; i < parsed.Count; i++)
            {
                bounds = SKRect.Union(bounds, parsed[i].Path.Bounds);
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return [];
            }

            var result = new List<Shape>();
            foreach (var (path, fill) in parsed)
            {
                using var normalized = new SKPath(path);
                normalized.Offset(-bounds.Left, -bounds.Top);
                normalized.Transform(SKMatrix.CreateScale(24f / bounds.Width, 24f / bounds.Height));

                result.Add(new Shape
                {
                    Kind = ShapeKind.CombinedPath,
                    X = placeX,
                    Y = placeY,
                    Width = placeWidth,
                    Height = placeHeight,
                    FillColor = fill ?? defaultFillColor,
                    BorderColor = borderColor,
                    BorderThickness = borderThickness,
                    CustomPathData = normalized.ToSvgPathData(),
                    CustomPathStrokeOnly = strokeOnly,
                });
            }

            return result;
        }
        finally
        {
            foreach (var (path, _) in parsed)
            {
                path.Dispose();
            }
        }
    }
}
