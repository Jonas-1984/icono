using System.IO;
using System.Windows;
using Icono.IO;
using Icono.Editor;

namespace Icono.App.Views;

/// <summary>The icons that come with icono and are always in the MY ICONS card (next to the ones the user saved): they are SVG files inside the
/// program, read like any imported SVG (<see cref="IconLibrary.BuiltInFromSvg"/>), so every piece is an ordinary editable path.</summary>
public static class BuiltInIcons
{
    private static IReadOnlyList<IconLibraryEntry>? _all;

    public static IReadOnlyList<IconLibraryEntry> All => _all ??= Load();

    private static List<IconLibraryEntry> Load()
    {
        var entries = new List<IconLibraryEntry>();
        if (Read("pack://application:,,,/Assets/Samples/icono-logo.svg", "icono logo") is { } logo)
        {
            entries.Add(logo);
        }

        return entries;
    }

    private static IconLibraryEntry? Read(string address, string name)
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri(address));
            if (resource is null)
            {
                return null;
            }

            using var reader = new StreamReader(resource.Stream);
            return IconLibrary.BuiltInFromSvg(reader.ReadToEnd(), name);
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
