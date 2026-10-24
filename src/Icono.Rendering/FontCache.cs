using System.Collections.Concurrent;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Typefaces looked up by name are slow to resolve and every call hands out a new native object. Dimensions and labels draw
/// several texts per frame, so they share one typeface per (family, bold, italic). The typefaces live for the whole process and
/// must NOT be disposed by the callers.</summary>
public static class FontCache
{
    private static readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), SKTypeface> Typefaces = new();

    public static SKTypeface Get(string family, bool bold = false, bool italic = false)
    {
        var name = string.IsNullOrWhiteSpace(family) ? "Segoe UI" : family;
        return Typefaces.GetOrAdd((name, bold, italic), key => SKTypeface.FromFamilyName(
            key.Family,
            key.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            key.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright));
    }
}
