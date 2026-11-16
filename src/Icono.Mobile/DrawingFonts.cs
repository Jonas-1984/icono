using Avalonia.Platform;
using Icono.Rendering;
using SkiaSharp;

namespace Icono.Mobile;

/// <summary>Drawings made on Windows name Windows fonts (Segoe UI above all). Where the tablet lacks one, the drawing is rendered with
/// Selawik, Microsoft's open replacement for Segoe UI with the same letter widths, so texts, dimensions and title blocks look and
/// wrap as on Windows. Selawik has no italic: italic text uses the system sans-serif in italic.</summary>
public static class DrawingFonts
{
    private static SKTypeface? _regular;
    private static SKTypeface? _bold;

    public static void Install()
    {
        _regular = Load("selawk.ttf");
        _bold = Load("selawkb.ttf");
        FontCache.MissingFamilySubstitute = Substitute;
    }

    private static SKTypeface? Substitute(bool bold, bool italic)
    {
        if (italic)
        {
            return SKFontManager.Default.MatchFamily("sans-serif", new SKFontStyle(
                bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic));
        }

        return bold ? _bold : _regular;
    }

    private static SKTypeface? Load(string file)
    {
        using var stream = AssetLoader.Open(new Uri("avares://Icono.Mobile/Assets/Fonts/" + file));
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data);
    }
}
