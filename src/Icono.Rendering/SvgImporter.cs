using System.Text.RegularExpressions;
using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>
/// Turns pasted SVG markup (or bare path "d" data) into one or more <see cref="ShapeKind.CombinedPath"/>
/// shapes. When the markup has several &lt;path&gt; elements (e.g. a background shape plus a
/// foreground glyph), every path is normalized against ONE SHARED bounding box instead of its own —
/// so splitting the icon into independently colorable shapes doesn't destroy how they line up.
/// </summary>
public static class SvgImporter
{
    private static readonly Regex PathDataRegex = new(
        """d\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Extracts every &lt;path d="..."&gt; value from <paramref name="svgMarkup"/>, or —
    /// if no &lt;path&gt; tags are found — treats the whole trimmed input as one bare path's data
    /// (so pasting just the "d" value works too).</summary>
    public static IReadOnlyList<string> ExtractPathData(string svgMarkup)
    {
        var matches = PathDataRegex.Matches(svgMarkup);
        if (matches.Count > 0)
        {
            return matches.Select(m => m.Groups[1].Value).ToList();
        }

        var trimmed = svgMarkup.Trim();
        return trimmed.Length > 0 ? [trimmed] : [];
    }

    /// <summary>Parses each path's data, computes one shared bounding box across all of them, and
    /// returns a CombinedPath <see cref="Shape"/> per path — all sharing the same placement
    /// (X/Y/Width/Height), each normalized into that shared 24x24 space (mirroring
    /// <see cref="ShapeCombiner"/>'s convention) so their relative position/scale survives the
    /// split. Returns an empty list if nothing in <paramref name="pathDataStrings"/> parses to a
    /// non-degenerate path.</summary>
    public static IReadOnlyList<Shape> BuildShapes(
        IReadOnlyList<string> pathDataStrings, ArgbColor fillColor, ArgbColor borderColor,
        double borderThickness, double placeX, double placeY, double placeWidth, double placeHeight)
    {
        var parsed = new List<SKPath>();

        try
        {
            foreach (var d in pathDataStrings)
            {
                var path = SKPath.ParseSvgPathData(d);
                if (path is not null)
                {
                    parsed.Add(path);
                }
            }

            if (parsed.Count == 0)
            {
                return [];
            }

            var bounds = parsed[0].Bounds;
            for (var i = 1; i < parsed.Count; i++)
            {
                bounds = SKRect.Union(bounds, parsed[i].Bounds);
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return [];
            }

            var result = new List<Shape>();
            foreach (var path in parsed)
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
                    FillColor = fillColor,
                    BorderColor = borderColor,
                    BorderThickness = borderThickness,
                    CustomPathData = normalized.ToSvgPathData(),
                });
            }

            return result;
        }
        finally
        {
            foreach (var path in parsed)
            {
                path.Dispose();
            }
        }
    }
}
