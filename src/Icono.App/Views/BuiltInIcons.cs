using System.IO;
using System.Windows;
using Icono.Core;
using Icono.IO;
using Icono.Rendering;

namespace Icono.App.Views;

/// <summary>The icons that come with icono and are always in the MY ICONS card (next to the ones the user saved): they are SVG files inside the
/// program, read like any imported SVG, so every piece is an ordinary editable path.</summary>
public static class BuiltInIcons
{
    /// <summary>How wide the logo is made when it is read (page units); it is fitted to the page when it is inserted.</summary>
    private const double LogoWidth = 300;

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
            var elements = SvgImporter.ExtractElements(reader.ReadToEnd());
            if (elements.Count == 0 || SvgImporter.MeasureBounds(elements.Select(e => e.PathData).ToList()) is not { } size)
            {
                return null;
            }

            var height = LogoWidth * size.Height / size.Width;
            var colour = ArgbColor.FromRgb(0x10, 0x3C, 0x9E);
            var shapes = SvgImporter.BuildShapes(elements, colour, colour, 0, 0, 0, LogoWidth, height);
            var group = Guid.NewGuid();
            foreach (var shape in shapes)
            {
                shape.GroupId = group;
            }

            return shapes.Count == 0 ? null : new IconLibraryEntry { Name = name, Shapes = shapes };
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Moves a copy of the shapes of a built-in icon to the middle of the page, as wide as 60 % of the page (but not higher than 80 %),
    /// with a group of their own, so inserting it twice gives two icons that move on their own.</summary>
    public static void PlaceOnPage(IReadOnlyList<Shape> shapes, double pageLeft, double pageTop, double pageWidth, double pageHeight)
    {
        if (shapes.Count == 0)
        {
            return;
        }

        var width = shapes.Max(s => s.X + s.Width) - shapes.Min(s => s.X);
        var height = shapes.Max(s => s.Y + s.Height) - shapes.Min(s => s.Y);
        var left = shapes.Min(s => s.X);
        var top = shapes.Min(s => s.Y);
        var factor = Math.Min(0.6 * pageWidth / width, 0.8 * pageHeight / height);
        var x = pageLeft + (pageWidth - width * factor) / 2;
        var y = pageTop + (pageHeight - height * factor) / 2;
        var group = Guid.NewGuid();
        foreach (var shape in shapes)
        {
            shape.X = x + (shape.X - left) * factor;
            shape.Y = y + (shape.Y - top) * factor;
            shape.Width *= factor;
            shape.Height *= factor;
            shape.GroupId = group;
        }
    }
}
