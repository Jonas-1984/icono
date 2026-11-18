using Icono.Core;
using Icono.Editor.Undo;
using Icono.IO;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>The MY ICONS library, shared by the Windows program and the tablet app: icons that come with icono (read from SVG), icons the user saved
/// from a selection (<see cref="IconLibraryStore"/>), and putting one of them into the drawing as a copy.</summary>
public static class IconLibrary
{
    /// <summary>How wide an icon that comes with icono is made when it is read (page units); it is fitted to the page when it is inserted.</summary>
    private const double BuiltInWidth = 300;

    /// <summary>Reads an icon that comes with icono from SVG code: every piece is an ordinary editable path, together they are one group.
    /// Null when the code holds no path.</summary>
    public static IconLibraryEntry? BuiltInFromSvg(string svgMarkup, string name)
    {
        var elements = SvgImporter.ExtractElements(svgMarkup);
        if (elements.Count == 0 || SvgImporter.MeasureBounds(elements.Select(e => e.PathData).ToList()) is not { } size)
        {
            return null;
        }

        var height = BuiltInWidth * size.Height / size.Width;
        var colour = ArgbColor.FromRgb(0x10, 0x3C, 0x9E);
        var shapes = SvgImporter.BuildShapes(elements, colour, colour, 0, 0, 0, BuiltInWidth, height);
        var group = Guid.NewGuid();
        foreach (var shape in shapes)
        {
            shape.GroupId = group;
        }

        return shapes.Count == 0 ? null : new IconLibraryEntry { Name = name, Shapes = shapes };
    }

    /// <summary>A copy of a shape that shares nothing with it.</summary>
    public static Shape Clone(Shape source)
    {
        var clone = new Shape { Kind = source.Kind };
        ShapeSnapshot.Capture(source).ApplyTo(clone);
        return clone;
    }

    /// <summary>Moves the shapes to the middle of the page, as wide as 60 % of the page (but not higher than 80 %), with a group of their own, so
    /// inserting an icon twice gives two icons that move on their own.</summary>
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

    /// <summary>Puts a copy of the icon's shapes into the selected layer (on top), selects them, as one undo step. An icon that comes with icono has
    /// no place of its own (<paramref name="builtIn"/>): it lands in the middle of the current page. Empty when there is no layer.</summary>
    public static IReadOnlyList<Shape> Insert(EditorContext context, IconLibraryEntry entry, bool builtIn)
    {
        var layer = context.SelectedLayer ?? context.Document.Layers.FirstOrDefault();
        if (layer is null)
        {
            return [];
        }

        var copies = entry.Shapes.Select(Clone).ToList();
        if (builtIn)
        {
            PlaceOnPage(copies, 0, context.Document.PageTop(context.CurrentPage), context.Document.Width, context.Document.Height);
        }

        if (copies.Count == 0)
        {
            return copies;
        }

        // every shape goes in at index 0 (front-most) at the moment of its own insertion, so that is the redo index for each
        var actions = new List<UndoableAction>();
        foreach (var shape in copies)
        {
            layer.Shapes.Insert(0, shape);
            actions.Add(UndoableAction.ForInsert(layer.Shapes, shape, 0));
        }

        context.UndoManager.Push(UndoableAction.Combine(actions));
        context.SelectedShapes.Clear();
        foreach (var shape in copies)
        {
            context.SelectedShapes.Add(shape);
        }

        context.SelectedLayer = layer;
        context.RequestRedraw();
        return copies;
    }

    /// <summary>Saves the selection as an icon of the user - expanded to every other shape that shares a group with something selected (a click can
    /// only hit the topmost shape, but a group of pieces on top of each other reads as one icon). Null when nothing is selected.</summary>
    public static IconLibraryEntry? SaveSelection(EditorContext context, IconLibraryStore store)
    {
        if (context.SelectedShapes.Count == 0)
        {
            return null;
        }

        var groupIds = context.SelectedShapes.Select(s => s.GroupId).Where(id => id is not null).ToHashSet();
        var shapes = context.SelectedShapes
            .Concat(context.Document.Layers.SelectMany(l => l.Shapes).Where(s => s.GroupId is not null && groupIds.Contains(s.GroupId)))
            .Distinct()
            .ToList();

        var entry = new IconLibraryEntry { Name = $"Icon {DateTime.Now:HH:mm:ss}", Shapes = shapes.Select(Clone).ToList() };
        store.Save(entry);
        return entry;
    }
}
