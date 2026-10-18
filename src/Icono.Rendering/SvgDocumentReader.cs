using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// A real (XML based) reader for SVG artwork. Compared with the plain regex scan it understands what multi-colour
/// brand marks need: nested groups with <c>transform</c>, <c>clip-path</c> (applied as a true path intersection),
/// inherited and per-element fills, <c>opacity</c>, and <c>linearGradient</c> / <c>radialGradient</c> fills (with
/// <c>xlink:href</c> chains, <c>gradientUnits</c> and <c>gradientTransform</c>). Content of <c>&lt;defs&gt;</c>,
/// <c>&lt;clipPath&gt;</c>, masks etc. is never drawn. Every element comes out with its geometry already baked into
/// the document's root coordinate system.
/// </summary>
internal sealed class SvgDocumentReader
{
    private static readonly HashSet<string> SkippedContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "defs", "clippath", "mask", "symbol", "marker", "pattern", "lineargradient", "radialgradient", "filter",
        "title", "desc", "style", "metadata", "script", "text", "image", "foreignobject",
    };

    private readonly Dictionary<string, XElement> _ids = new();
    private double _viewWidth = 100;
    private double _viewHeight = 100;

    /// <summary>The visual state inherited down the element tree.</summary>
    private sealed record Ctx(SKMatrix Matrix, string? Fill, double Opacity, double FillOpacity, SKPath? Clip);

    /// <summary>Reads <paramref name="markup"/>; null when it is not parseable SVG/XML or holds nothing drawable (the caller
    /// then falls back to the simple regex scan, which also handles a bare path "d" value).</summary>
    public static List<SvgImporter.SvgElement>? Read(string markup)
    {
        var root = Parse(markup);
        if (root is null)
        {
            return null;
        }

        var reader = new SvgDocumentReader();
        return reader.Run(root);
    }

    private static XElement? Parse(string markup)
    {
        var text = markup.Trim();
        if (!text.StartsWith('<'))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(text).Root;
        }
        catch (System.Xml.XmlException)
        {
            // Pasted fragments (several bare elements, or an undeclared xlink prefix) — wrap them in a root.
            try
            {
                return XDocument.Parse(
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">" + text + "</svg>").Root;
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }
    }

    private List<SvgImporter.SvgElement>? Run(XElement root)
    {
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Attribute("id") is { } id && !_ids.ContainsKey(id.Value))
            {
                _ids[id.Value] = element;
            }
        }

        if (root.Attribute("viewBox") is { } viewBox)
        {
            var parts = Numbers(viewBox.Value);
            if (parts.Count == 4 && parts[2] > 0 && parts[3] > 0)
            {
                _viewWidth = parts[2];
                _viewHeight = parts[3];
            }
        }

        var result = new List<SvgImporter.SvgElement>();
        Walk(root, new Ctx(SKMatrix.CreateIdentity(), null, 1, 1, null), result);

        // Clip paths created along the way are owned by the contexts; nothing else to release.
        return result.Count > 0 ? result : null;
    }

    private void Walk(XElement element, Ctx parent, List<SvgImporter.SvgElement> output)
    {
        var tag = element.Name.LocalName.ToLowerInvariant();
        if (SkippedContainers.Contains(tag) || Prop(element, "display") == "none")
        {
            return;
        }

        var matrix = parent.Matrix;
        if (element.Attribute("transform") is { } transform)
        {
            matrix = matrix.PreConcat(ParseTransform(transform.Value));
        }

        var fill = Prop(element, "fill") ?? parent.Fill;
        var opacity = parent.Opacity * (Number(Prop(element, "opacity")) ?? 1);
        var fillOpacity = parent.FillOpacity * (Number(Prop(element, "fill-opacity")) ?? 1);

        var clip = parent.Clip;
        if (UrlId(Prop(element, "clip-path")) is { } clipId && BuildClip(clipId, matrix) is { } clipPath)
        {
            clip = clip is null ? clipPath : clip.Op(clipPath, SKPathOp.Intersect) ?? clipPath;
        }

        var ctx = new Ctx(matrix, fill, opacity, fillOpacity, clip);

        if (tag is "path" or "circle" or "ellipse" or "rect" or "polygon" or "polyline")
        {
            Emit(element, tag, ctx, output);
            return;
        }

        foreach (var child in element.Elements())
        {
            Walk(child, ctx, output);
        }
    }

    private void Emit(XElement element, string tag, Ctx ctx, List<SvgImporter.SvgElement> output)
    {
        using var local = BuildLocalPath(element, tag, out var originalData);
        if (local is null || local.IsEmpty)
        {
            return;
        }

        var localBounds = local.Bounds;

        // Untouched geometry keeps its original path text (no pointless re-serialization).
        var identityUnclipped = ctx.Matrix.IsIdentity && ctx.Clip is null;
        var path = new SKPath(local);
        path.Transform(ctx.Matrix);

        if (ctx.Clip is not null)
        {
            var clipped = path.Op(ctx.Clip, SKPathOp.Intersect);
            path.Dispose();
            if (clipped is null || clipped.IsEmpty)
            {
                clipped?.Dispose();
                return;
            }

            path = clipped;
        }

        using (path)
        {
            ArgbColor? color = null;
            GradientFill? gradient = null;

            var raw = ctx.Fill?.Trim();
            if (!string.IsNullOrEmpty(raw) && raw is not ("none" or "transparent" or "inherit"))
            {
                if (UrlId(raw) is { } gradientId)
                {
                    (color, gradient) = BuildGradient(gradientId, ctx.Matrix, localBounds);
                }
                else if (!raw.Equals("currentColor", StringComparison.OrdinalIgnoreCase) && SvgImporter.TryParseColor(raw, out var parsed))
                {
                    color = parsed;
                }
            }

            output.Add(new SvgImporter.SvgElement(
                identityUnclipped && originalData is not null ? originalData : path.ToSvgPathData(),
                color, gradient, Math.Clamp(ctx.Opacity * ctx.FillOpacity, 0, 1)));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // geometry
    // ---------------------------------------------------------------------------------------------

    private static SKPath? BuildLocalPath(XElement element, string tag) => BuildLocalPath(element, tag, out _);

    private static SKPath? BuildLocalPath(XElement element, string tag, out string? pathData)
    {
        pathData = null;
        string? d;
        if (tag is "polygon" or "polyline")
        {
            var points = Numbers(element.Attribute("points")?.Value ?? string.Empty);
            if (points.Count < 4)
            {
                return null;
            }

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i + 1 < points.Count; i += 2)
            {
                sb.Append(FormattableString.Invariant($"{(i == 0 ? 'M' : 'L')}{points[i]},{points[i + 1]} "));
            }

            if (tag == "polygon")
            {
                sb.Append('Z');
            }

            d = sb.ToString();
        }
        else
        {
            var attrs = element.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
            d = tag switch
            {
                "path" => attrs.GetValueOrDefault("d"),
                "circle" => SvgImporter.CirclePathData(attrs),
                "ellipse" => SvgImporter.EllipsePathData(attrs),
                "rect" => SvgImporter.RectPathData(attrs),
                _ => null,
            };
        }

        pathData = d;
        return string.IsNullOrWhiteSpace(d) ? null : SKPath.ParseSvgPathData(d);
    }

    /// <summary>A clipPath's outline (union of its children) in root space, for an element whose own coordinate system
    /// (including its transform) is <paramref name="elementMatrix"/>.</summary>
    private SKPath? BuildClip(string id, SKMatrix elementMatrix)
    {
        if (!_ids.TryGetValue(id, out var clipElement) || !clipElement.Name.LocalName.Equals("clipPath", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Prop(clipElement, "clipPathUnits") == "objectBoundingBox")
        {
            return null;
        }

        var baseMatrix = elementMatrix;
        if (clipElement.Attribute("transform") is { } clipTransform)
        {
            baseMatrix = baseMatrix.PreConcat(ParseTransform(clipTransform.Value));
        }

        SKPath? union = null;
        foreach (var child in clipElement.Elements())
        {
            var tag = child.Name.LocalName.ToLowerInvariant();
            if (tag is not ("path" or "circle" or "ellipse" or "rect" or "polygon" or "polyline"))
            {
                continue;
            }

            using var local = BuildLocalPath(child, tag);
            if (local is null)
            {
                continue;
            }

            var matrix = baseMatrix;
            if (child.Attribute("transform") is { } childTransform)
            {
                matrix = matrix.PreConcat(ParseTransform(childTransform.Value));
            }

            var rooted = new SKPath(local);
            rooted.Transform(matrix);

            if (union is null)
            {
                union = rooted;
            }
            else
            {
                var merged = union.Op(rooted, SKPathOp.Union);
                union.Dispose();
                rooted.Dispose();
                union = merged;
            }
        }

        return union;
    }

    // ---------------------------------------------------------------------------------------------
    // gradients
    // ---------------------------------------------------------------------------------------------

    private (ArgbColor? Color, GradientFill? Gradient) BuildGradient(string id, SKMatrix elementMatrix, SKRect localBounds)
    {
        if (!_ids.TryGetValue(id, out var first))
        {
            return (null, null);
        }

        var kindLinear = first.Name.LocalName.Equals("linearGradient", StringComparison.OrdinalIgnoreCase);
        if (!kindLinear && !first.Name.LocalName.Equals("radialGradient", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        // Follow xlink:href so a gradient can inherit stops and attributes from another one.
        var chain = new List<XElement>();
        for (var current = first; current is not null && !chain.Contains(current) && chain.Count < 12;)
        {
            chain.Add(current);
            var href = current.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;
            current = href is { Length: > 1 } && href[0] == '#' && _ids.TryGetValue(href[1..], out var next) ? next : null;
        }

        string? Inherited(string name) => chain.Select(e => e.Attribute(name)?.Value).FirstOrDefault(v => v is not null);

        var stops = new List<GradientStop>();
        foreach (var element in chain)
        {
            foreach (var stop in element.Elements().Where(e => e.Name.LocalName == "stop"))
            {
                var offset = Fraction(stop.Attribute("offset")?.Value) ?? 0;
                if (stops.Count > 0)
                {
                    offset = Math.Max(offset, stops[^1].Offset);
                }

                var colorText = Prop(stop, "stop-color") ?? "#000000";
                if (!SvgImporter.TryParseColor(colorText.Trim(), out var color))
                {
                    color = ArgbColor.FromRgb(0, 0, 0);
                }

                var stopOpacity = Number(Prop(stop, "stop-opacity")) ?? 1;
                color = new ArgbColor((byte)Math.Clamp(color.A * stopOpacity, 0, 255), color.R, color.G, color.B);
                stops.Add(new GradientStop(Math.Clamp(offset, 0, 1), color));
            }

            if (stops.Count > 0)
            {
                break;
            }
        }

        if (stops.Count == 0)
        {
            return (null, null);
        }

        var average = AverageColor(stops);
        if (stops.Count == 1)
        {
            return (average, null);
        }

        var boundingBoxUnits = Inherited("gradientUnits") != "userSpaceOnUse";
        double Coordinate(string name, double bboxDefault, double userDefault)
        {
            var raw = Inherited(name);
            if (raw is null)
            {
                return boundingBoxUnits ? bboxDefault : userDefault;
            }

            var value = Fraction(raw, percentAsFraction: true);
            return value ?? (boundingBoxUnits ? bboxDefault : userDefault);
        }

        var matrix = elementMatrix;
        if (Inherited("gradientTransform") is { } gradientTransform)
        {
            matrix = matrix.PreConcat(ParseTransform(gradientTransform));
        }

        if (boundingBoxUnits)
        {
            matrix = matrix.PreConcat(new SKMatrix
            {
                ScaleX = Math.Max(localBounds.Width, 1e-6f), ScaleY = Math.Max(localBounds.Height, 1e-6f),
                TransX = localBounds.Left, TransY = localBounds.Top, Persp2 = 1,
            });
        }

        GradientFill gradient;
        if (kindLinear)
        {
            gradient = new GradientFill(
                GradientKind.Linear,
                Coordinate("x1", 0, 0), Coordinate("y1", 0, 0), Coordinate("x2", 1, _viewWidth), Coordinate("y2", 0, 0),
                0, 0, 0, 0, 0,
                matrix.ScaleX, matrix.SkewY, matrix.SkewX, matrix.ScaleY, matrix.TransX, matrix.TransY, stops);
        }
        else
        {
            var cx = Coordinate("cx", 0.5, _viewWidth / 2);
            var cy = Coordinate("cy", 0.5, _viewHeight / 2);
            var r = Coordinate("r", 0.5, Math.Max(_viewWidth, _viewHeight) / 2);
            var fx = Inherited("fx") is null ? cx : Coordinate("fx", cx, cx);
            var fy = Inherited("fy") is null ? cy : Coordinate("fy", cy, cy);
            gradient = new GradientFill(
                GradientKind.Radial, 0, 0, 0, 0, cx, cy, r, fx, fy,
                matrix.ScaleX, matrix.SkewY, matrix.SkewX, matrix.ScaleY, matrix.TransX, matrix.TransY, stops);
        }

        return (average, gradient);
    }

    private static ArgbColor AverageColor(IReadOnlyList<GradientStop> stops)
    {
        double a = 0, r = 0, g = 0, b = 0;
        foreach (var stop in stops)
        {
            a += stop.Color.A;
            r += stop.Color.R;
            g += stop.Color.G;
            b += stop.Color.B;
        }

        var n = stops.Count;
        return new ArgbColor((byte)(a / n), (byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    // ---------------------------------------------------------------------------------------------
    // attribute / number helpers
    // ---------------------------------------------------------------------------------------------

    private static readonly Regex StyleEntryRegex = new(@"([\w-]+)\s*:\s*([^;]+)", RegexOptions.Compiled);

    /// <summary>A presentation property: the inline <c>style</c> declaration wins over the plain attribute.</summary>
    private static string? Prop(XElement element, string name)
    {
        if (element.Attribute("style") is { } style)
        {
            foreach (Match match in StyleEntryRegex.Matches(style.Value))
            {
                if (match.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return match.Groups[2].Value.Trim();
                }
            }
        }

        return element.Attribute(name)?.Value;
    }

    private static readonly Regex UrlRegex = new(@"url\(\s*['""]?#([^)'""\s]+)['""]?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string? UrlId(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var match = UrlRegex.Match(value);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static readonly Regex NumberRegex = new(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    private static List<double> Numbers(string text) =>
        NumberRegex.Matches(text).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();

    private static double? Number(string? text) =>
        text is not null && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>"0.5" -> 0.5, "50%" -> 0.5 (percent values count as fractions of 1).</summary>
    private static double? Fraction(string? text, bool percentAsFraction = true)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.EndsWith('%'))
        {
            return Number(trimmed[..^1]) is { } percent && percentAsFraction ? percent / 100 : null;
        }

        return Number(trimmed);
    }

    private static readonly Regex TransformRegex = new(@"(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

    /// <summary>Parses an SVG transform list ("translate(..) rotate(..) matrix(..)") — operations apply left to right.</summary>
    internal static SKMatrix ParseTransform(string text)
    {
        var result = SKMatrix.CreateIdentity();
        foreach (Match match in TransformRegex.Matches(text))
        {
            var n = Numbers(match.Groups[2].Value);
            SKMatrix op;
            switch (match.Groups[1].Value.ToLowerInvariant())
            {
                case "matrix" when n.Count >= 6:
                    op = new SKMatrix
                    {
                        ScaleX = (float)n[0], SkewY = (float)n[1], SkewX = (float)n[2], ScaleY = (float)n[3],
                        TransX = (float)n[4], TransY = (float)n[5], Persp2 = 1,
                    };
                    break;
                case "translate" when n.Count >= 1:
                    op = SKMatrix.CreateTranslation((float)n[0], n.Count > 1 ? (float)n[1] : 0);
                    break;
                case "scale" when n.Count >= 1:
                    op = SKMatrix.CreateScale((float)n[0], n.Count > 1 ? (float)n[1] : (float)n[0]);
                    break;
                case "rotate" when n.Count >= 1:
                    op = n.Count >= 3
                        ? SKMatrix.CreateRotationDegrees((float)n[0], (float)n[1], (float)n[2])
                        : SKMatrix.CreateRotationDegrees((float)n[0]);
                    break;
                case "skewx" when n.Count >= 1:
                    op = SKMatrix.CreateSkew((float)Math.Tan(n[0] * Math.PI / 180), 0);
                    break;
                case "skewy" when n.Count >= 1:
                    op = SKMatrix.CreateSkew(0, (float)Math.Tan(n[0] * Math.PI / 180));
                    break;
                default:
                    continue;
            }

            result = result.PreConcat(op);
        }

        return result;
    }
}
