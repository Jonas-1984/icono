using System.Windows.Media.Imaging;
using Icono.IO;

namespace Icono.App.Views;

/// <summary>Wraps a Core/IO <see cref="IconLibraryEntry"/> with a rendered WPF thumbnail for the
/// library panel's ItemsControl, keeping Icono.IO free of any WPF dependency.</summary>
public sealed class IconLibraryListItem
{
    public IconLibraryEntry Entry { get; }

    public string Name => Entry.Name;

    public BitmapImage Thumbnail { get; }

    /// <summary>The on-disk JSON file this entry was loaded from — round-tripped straight into
    /// <see cref="IconLibraryStore.Delete"/> when the user removes it.</summary>
    public string FilePath { get; }

    public IconLibraryListItem(IconLibraryEntry entry, BitmapImage thumbnail, string filePath)
    {
        Entry = entry;
        Thumbnail = thumbnail;
        FilePath = filePath;
    }
}
