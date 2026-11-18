using Icono.Core;
using Icono.Editor;
using Icono.IO;

namespace Icono.Tests;

/// <summary>MY ICONS: icons that come with icono, icons saved from a selection and putting one into the drawing - the same for the Windows
/// program and the tablet app.</summary>
public class IconLibraryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "icono-icons-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private const string Svg = "<svg viewBox=\"0 0 24 24\"><path d=\"M2 2 L22 2 L22 12 L2 12 Z\" fill=\"#ff0000\"/><path d=\"M2 14 L22 14 L22 22 L2 22 Z\" fill=\"#0000ff\"/></svg>";

    private static EditorContext NewContext()
    {
        var context = new EditorContext();
        context.Document.Width = 800;
        context.Document.Height = 600;
        context.Document.Layers.Add(new Layer { Name = "Layer 1" });
        return context;
    }

    private static Shape Box(double x, double y, Guid? group = null) =>
        new() { Kind = ShapeKind.Rectangle, X = x, Y = y, Width = 40, Height = 30, GroupId = group };

    [Fact]
    public void ABuiltInIcon_ComesFromSvg_AsOneGroupOfEditablePaths()
    {
        var entry = IconLibrary.BuiltInFromSvg(Svg, "logo");

        Assert.NotNull(entry);
        Assert.Equal("logo", entry!.Name);
        Assert.Equal(2, entry.Shapes.Count);
        Assert.Single(entry.Shapes.Select(s => s.GroupId).Distinct());
        Assert.NotNull(entry.Shapes[0].GroupId);
    }

    [Fact]
    public void SvgWithoutAPath_GivesNoIcon() => Assert.Null(IconLibrary.BuiltInFromSvg("<svg></svg>", "nothing"));

    [Fact]
    public void InsertingABuiltInIcon_PutsACopyInTheMiddleOfThePage_SelectedAndGrouped()
    {
        var context = NewContext();
        var entry = IconLibrary.BuiltInFromSvg(Svg, "logo")!;

        var copies = IconLibrary.Insert(context, entry, builtIn: true);

        Assert.Equal(2, copies.Count);
        Assert.Equal(2, context.SelectedShapes.Count);
        Assert.DoesNotContain(copies[0], entry.Shapes);
        var left = copies.Min(s => s.X);
        var right = copies.Max(s => s.X + s.Width);
        Assert.Equal(400, (left + right) / 2, 1);
        Assert.Single(copies.Select(s => s.GroupId).Distinct());
        Assert.NotEqual(entry.Shapes[0].GroupId, copies[0].GroupId); // a group of its own: two inserts move on their own
    }

    [Fact]
    public void InsertingTwice_GivesTwoSeparateIcons()
    {
        var context = NewContext();
        var entry = IconLibrary.BuiltInFromSvg(Svg, "logo")!;

        var first = IconLibrary.Insert(context, entry, builtIn: true);
        var second = IconLibrary.Insert(context, entry, builtIn: true);

        Assert.NotEqual(first[0].GroupId, second[0].GroupId);
        Assert.Equal(4, context.Document.Layers[0].Shapes.Count);
    }

    [Fact]
    public void AnIconOfTheUser_KeepsItsPlace_AndInsertIsOneUndoStep()
    {
        var context = NewContext();
        var entry = new IconLibraryEntry { Name = "mine", Shapes = [Box(100, 120), Box(200, 220)] };

        var copies = IconLibrary.Insert(context, entry, builtIn: false);

        Assert.Equal(100, copies.Min(s => s.X));
        Assert.Equal(120, copies.Min(s => s.Y));
        context.Undo();
        Assert.Empty(context.Document.Layers[0].Shapes);
    }

    [Fact]
    public void SavingTheSelection_TakesTheWholeGroup_NotOnlyWhatWasClicked()
    {
        var context = NewContext();
        var group = Guid.NewGuid();
        var top = Box(10, 10, group);
        var below = Box(10, 10, group);
        var other = Box(300, 300);
        context.Document.Layers[0].Shapes.Add(top);
        context.Document.Layers[0].Shapes.Add(below);
        context.Document.Layers[0].Shapes.Add(other);
        context.SelectedShapes.Add(top);
        var store = new IconLibraryStore(_folder);

        var entry = IconLibrary.SaveSelection(context, store);

        Assert.NotNull(entry);
        Assert.Equal(2, entry!.Shapes.Count);
        var saved = Assert.Single(store.LoadAll());
        Assert.Equal(2, saved.Entry.Shapes.Count);
        Assert.DoesNotContain(top, saved.Entry.Shapes); // copies, not the shapes of the drawing
    }

    [Fact]
    public void SavingNothing_SavesNothing()
    {
        var store = new IconLibraryStore(_folder);

        Assert.Null(IconLibrary.SaveSelection(NewContext(), store));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void ASavedIcon_CanBeDeleted()
    {
        var context = NewContext();
        var shape = Box(10, 10);
        context.Document.Layers[0].Shapes.Add(shape);
        context.SelectedShapes.Add(shape);
        var store = new IconLibraryStore(_folder);
        IconLibrary.SaveSelection(context, store);

        store.Delete(store.LoadAll()[0].FilePath);

        Assert.Empty(store.LoadAll());
    }
}
