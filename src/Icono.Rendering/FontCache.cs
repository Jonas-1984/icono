using System.Collections.Concurrent;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Typefaces looked up by name are slow to resolve and every call hands out a new native object. Dimensions and labels draw
/// several texts per frame, so they share one typeface per (family, bold, italic). The typefaces live for the whole process and
/// must NOT be disposed by the callers.</summary>
public static class FontCache
{
    private static readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), SKTypeface> Typefaces = new();

    /// <summary>Optional: gives the typeface to use when a drawing asks for a family the device does not have (a Windows font such as
    /// "Segoe UI" on Android). Without it (the Windows program) Skia's own choice is kept, as before. Set it once at start-up.</summary>
    public static Func<bool, bool, SKTypeface?>? MissingFamilySubstitute { get; set; }

    public static SKTypeface Get(string family, bool bold = false, bool italic = false)
    {
        var name = string.IsNullOrWhiteSpace(family) ? "Segoe UI" : family;
        return Typefaces.GetOrAdd((name, bold, italic), key => Resolve(key.Family, key.Bold, key.Italic));
    }

    private static SKTypeface Resolve(string family, bool bold, bool italic)
    {
        var typeface = SKTypeface.FromFamilyName(
            family,
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        var missing = typeface is null || !string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase);
        if (missing && MissingFamilySubstitute?.Invoke(bold, italic) is { } substitute)
        {
            typeface?.Dispose();
            return substitute;
        }

        return typeface ?? SKTypeface.Default;
    }
}
