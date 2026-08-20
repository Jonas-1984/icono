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

    public IconLibraryListItem(IconLibraryEntry entry, BitmapImage thumbnail)
    {
        Entry = entry;
        Thumbnail = thumbnail;
    }
}
