using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using SkiaSharp;

namespace Icono.Mobile;

public enum ThemeMode
{
    Dark,
    Light,
}

public enum ThemeStyle
{
    /// <summary>The usual look: soft gradients and soft keys.</summary>
    Soft,

    /// <summary>Flat and calm: no gradients, the drawing comes first.</summary>
    Pro,

    /// <summary>See-through plates over a colourful background. Only plain translucent colours, no blur - it costs neither speed nor memory.</summary>
    Glass,
}

/// <summary>The colour design of the tablet app, as in the Windows program: a mode (Dark or Light) and a style (Soft, Pro or Glass). Both are kept in a small file of the
/// app's data folder and applied as a theme variant of Avalonia; the colour keys are in App.axaml, everything refers to them by DynamicResource, so a change shows at once.</summary>
public static class AppTheme
{
    public static readonly ThemeVariant DarkPro = new("DarkPro", ThemeVariant.Dark);
    public static readonly ThemeVariant LightPro = new("LightPro", ThemeVariant.Light);
    public static readonly ThemeVariant DarkGlass = new("DarkGlass", ThemeVariant.Dark);
    public static readonly ThemeVariant LightGlass = new("LightGlass", ThemeVariant.Light);

    public static ThemeMode Mode { get; private set; } = ThemeMode.Dark;

    public static ThemeStyle Style { get; private set; } = ThemeStyle.Soft;

    public static bool IsLight => Mode == ThemeMode.Light;

    /// <summary>Raised after the look was changed (windows that built brushes in code build them again).</summary>
    public static event Action? Changed;

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icono-theme.txt");

    public static ThemeVariant VariantFor(ThemeMode mode, ThemeStyle style) => (mode, style) switch
    {
        (ThemeMode.Light, ThemeStyle.Pro) => LightPro,
        (ThemeMode.Light, ThemeStyle.Glass) => LightGlass,
        (ThemeMode.Light, _) => ThemeVariant.Light,
        (_, ThemeStyle.Pro) => DarkPro,
        (_, ThemeStyle.Glass) => DarkGlass,
        _ => ThemeVariant.Dark,
    };

    /// <summary>Reads the kept look (before the first window is built) and applies it.</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var parts = File.ReadAllText(FilePath).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && Enum.TryParse<ThemeMode>(parts[0], true, out var mode))
                {
                    Mode = mode;
                }

                if (parts.Length >= 2 && Enum.TryParse<ThemeStyle>(parts[1], true, out var style))
                {
                    Style = style;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not readable: the usual dark look
        }

        Apply(notify: false);
    }

    public static void Set(ThemeMode mode, ThemeStyle style)
    {
        if (mode == Mode && style == Style)
        {
            return;
        }

        Mode = mode;
        Style = style;
        Apply(notify: true);
        try
        {
            File.WriteAllText(FilePath, $"{mode} {style}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the look still holds until the app is left
        }
    }

    private static void Apply(bool notify)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = VariantFor(Mode, Style);
        }

        if (notify)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>A brush of the current look by its key (for controls that are built in code).</summary>
    public static IBrush Brush(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    // ---- colours of what is painted with Skia (the rulers, the labels on the canvas) ----

    public static SKColor RulerBack => IsLight ? new SKColor(0xF3, 0xF6, 0xFA, 0xF0) : new SKColor(0x14, 0x17, 0x1E, 0xF0);

    public static SKColor RulerInk => IsLight ? new SKColor(0x5C, 0x68, 0x7C) : new SKColor(0x8A, 0x94, 0xA6);

    public static SKColor RulerText => IsLight ? new SKColor(0x1B, 0x22, 0x33) : new SKColor(0xE9, 0xF0, 0xFA);

    public static SKColor RulerAccent => IsLight ? new SKColor(0x1D, 0x78, 0xE0) : new SKColor(0x5A, 0xB0, 0xFF);
}
