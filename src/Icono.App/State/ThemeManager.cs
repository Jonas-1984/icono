using System.IO;
using System.Windows;

namespace Icono.App.State;

public enum ThemeMode
{
    Dark,
    Light,
}

/// <summary>
/// Switches the whole UI between the Dark and Light colour palettes at runtime. All colour-dependent
/// resources live in Themes/DarkPalette.xaml and Themes/LightPalette.xaml under identical keys and are
/// referenced with DynamicResource, so replacing the palette dictionary re-skins every open window.
/// The user's choice is remembered in %LocalAppData%\Icono\theme.txt.
/// </summary>
public static class ThemeManager
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Icono", "theme.txt");

    public static ThemeMode Current { get; private set; } = ThemeMode.Dark;

    public static event EventHandler? ThemeChanged;

    /// <summary>Applies the saved theme (Dark when nothing is saved). Call before any window is created.</summary>
    public static void LoadSaved()
    {
        var mode = ThemeMode.Dark;

        try
        {
            if (File.Exists(SettingsFile) && Enum.TryParse<ThemeMode>(File.ReadAllText(SettingsFile).Trim(), out var saved))
            {
                mode = saved;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        Apply(mode, save: false);
    }

    public static void Apply(ThemeMode mode, bool save = true)
    {
        var merged = Application.Current.Resources.MergedDictionaries;

        var previous = merged.FirstOrDefault(d => d.Source?.OriginalString.EndsWith("Palette.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (previous is not null)
        {
            merged.Remove(previous);
        }

        merged.Add(new ResourceDictionary { Source = new Uri($"Themes/{mode}Palette.xaml", UriKind.Relative) });

        Current = mode;

        if (save)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, mode.ToString());
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
