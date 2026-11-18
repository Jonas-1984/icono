using Avalonia;
using Avalonia.Platform;
using Icono.Editor;
using Icono.IO;

namespace Icono.Mobile.Views;

/// <summary>The MY ICONS of the tablet app: the icons that come with icono (the logo, an SVG inside the app) and the ones the user saved from a
/// selection - kept in the app's own data folder (<see cref="IconLibraryStore"/>), the logic is <see cref="IconLibrary"/>, shared with Windows.</summary>
public static class MyIcons
{
    public static IconLibraryStore Store { get; } = new();

    private static IconLibraryEntry? _logo;

    /// <summary>One icon of the list: the file it is saved in (empty for an icon that comes with icono, which cannot be removed).</summary>
    public sealed record Icon(IconLibraryEntry Entry, string FilePath)
    {
        public bool IsBuiltIn => FilePath.Length == 0;
    }

    /// <summary>The icons that come with icono, then the saved ones (newest names last as the files are listed).</summary>
    public static IReadOnlyList<Icon> Load()
    {
        var icons = new List<Icon>();
        if (Logo() is { } logo)
        {
            icons.Add(new Icon(logo, string.Empty));
        }

        try
        {
            icons.AddRange(Store.LoadAll().OrderBy(e => File.GetCreationTimeUtc(e.FilePath)).Select(e => new Icon(e.Entry, e.FilePath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the folder cannot be read: only the icons that come with icono are shown
        }

        return icons;
    }

    private static IconLibraryEntry? Logo()
    {
        if (_logo is not null)
        {
            return _logo;
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Icono.Mobile/Assets/Samples/icono-logo.svg"));
            using var reader = new StreamReader(stream);
            return _logo = IconLibrary.BuiltInFromSvg(reader.ReadToEnd(), "icono logo");
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
