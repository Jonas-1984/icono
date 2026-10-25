using System.IO;
using System.Windows;

namespace Icono.App.State;

public enum ThemeMode
{
    Dark,
    Light,
}

/// <summary>The look of the surfaces: the soft, neumorphic keys, or the translucent "liquid glass" of recent phone systems.</summary>
public enum ThemeStyle
{
    Soft,
    Glass,
}

/// <summary>
/// Switches the whole UI between the colour palettes at runtime: Dark and Light, each in the Soft and in the Glass style. All
/// colour-dependent resources live in Themes/DarkPalette.xaml, LightPalette.xaml, GlassDarkPalette.xaml and GlassLightPalette.xaml
/// under identical keys and are referenced with DynamicResource, so replacing the palette dictionary re-skins every open window.
/// The user's choice is remembered in %LocalAppData%\Icono\theme.txt ("Dark", "Light", "GlassDark" or "GlassLight").
/// </summary>
public static class ThemeManager
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Icono", "theme.txt");

    public static ThemeMode Current { get; private set; } = ThemeMode.Dark;

    public static ThemeStyle Style { get; private set; } = ThemeStyle.Soft;

    public static event EventHandler? ThemeChanged;

    /// <summary>The name of the palette file for a mode and style.</summary>
    public static string PaletteName(ThemeMode mode, ThemeStyle style) => style == ThemeStyle.Glass ? $"Glass{mode}" : mode.ToString();

    /// <summary>Reads "Dark", "Light", "GlassDark" or "GlassLight"; false for anything else.</summary>
    public static bool TryParse(string? text, out ThemeMode mode, out ThemeStyle style)
    {
        mode = ThemeMode.Dark;
        style = ThemeStyle.Soft;
        var value = text?.Trim() ?? string.Empty;
        if (value.StartsWith("Glass", StringComparison.OrdinalIgnoreCase))
        {
            style = ThemeStyle.Glass;
            value = value[5..];
        }

        return Enum.TryParse(value, ignoreCase: true, out mode) && Enum.IsDefined(mode);
    }

    /// <summary>Applies the saved theme (Dark when nothing is saved). Call before any window is created.</summary>
    public static void LoadSaved()
    {
        var mode = ThemeMode.Dark;
        var style = ThemeStyle.Soft;

        try
        {
            if (File.Exists(SettingsFile) && TryParse(File.ReadAllText(SettingsFile), out var savedMode, out var savedStyle))
            {
                mode = savedMode;
                style = savedStyle;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        Apply(mode, style, save: false);
    }

    /// <summary>Changes the mode and keeps the style.</summary>
    public static void Apply(ThemeMode mode, bool save = true) => Apply(mode, Style, save);

    public static void Apply(ThemeMode mode, ThemeStyle style, bool save = true)
    {
        var merged = Application.Current.Resources.MergedDictionaries;

        var previous = merged.FirstOrDefault(d => d.Source?.OriginalString.EndsWith("Palette.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (previous is not null)
        {
            merged.Remove(previous);
        }

        merged.Add(new ResourceDictionary { Source = new Uri($"Themes/{PaletteName(mode, style)}Palette.xaml", UriKind.Relative) });

        Current = mode;
        Style = style;

        if (save)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, PaletteName(mode, style));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
