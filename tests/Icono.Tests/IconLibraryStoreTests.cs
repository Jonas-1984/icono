using Icono.Core;
using Icono.IO;

namespace Icono.Tests;

public class IconLibraryStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "IconoLibraryTests_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoadAll_RoundTripsAFixedGeometryShape()
    {
        var store = new IconLibraryStore(_tempDir);
        var entry = new IconLibraryEntry
        {
            Name = "My Icon",
            Shapes =
            [
                new Shape
                {
                    Kind = ShapeKind.Star,
                    X = 10,
                    Y = 20,
                    Width = 30,
                    Height = 40,
                    FillColor = ArgbColor.FromRgb(1, 2, 3),
                    CornerRadius = 5,
                    Rotation = 45,
                },
            ],
        };

        store.Save(entry);
        var loaded = store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("My Icon", loaded[0].Entry.Name);
        Assert.Single(loaded[0].Entry.Shapes);
        Assert.True(File.Exists(loaded[0].FilePath));

        var shape = loaded[0].Entry.Shapes[0];
        Assert.Equal(ShapeKind.Star, shape.Kind);
        Assert.Equal(10, shape.X);
        Assert.Equal(20, shape.Y);
        Assert.Equal(30, shape.Width);
        Assert.Equal(40, shape.Height);
        Assert.Equal(ArgbColor.FromRgb(1, 2, 3), shape.FillColor);
        Assert.Equal(5, shape.CornerRadius);
        Assert.Equal(45, shape.Rotation);
    }

    [Fact]
    public void SaveThenLoadAll_RoundTripsAPointsBasedShape()
    {
        var store = new IconLibraryStore(_tempDir);
        var entry = new IconLibraryEntry
        {
            Name = "Line Icon",
            Shapes =
            [
                new Shape
                {
                    Kind = ShapeKind.Polyline,
                    X = 0,
                    Y = 0,
                    Points = [new CorePoint(0, 0), new CorePoint(10, 5), new CorePoint(20, 0)],
                    Dashed = true,
                },
            ],
        };

        store.Save(entry);
        var loaded = store.LoadAll();

        var shape = Assert.Single(loaded[0].Entry.Shapes);
        Assert.NotNull(shape.Points);
        Assert.Equal(3, shape.Points!.Count);
        Assert.Equal(new CorePoint(10, 5), shape.Points[1]);
        Assert.True(shape.Dashed);
    }

    [Fact]
    public void LoadAll_OnMissingFolder_ReturnsEmpty()
    {
        var store = new IconLibraryStore(Path.Combine(_tempDir, "does-not-exist"));

        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Delete_RemovesTheEntrySoItNoLongerLoads()
    {
        var store = new IconLibraryStore(_tempDir);
        var entry = new IconLibraryEntry { Name = "Removable", Shapes = [new Shape { Kind = ShapeKind.Star }] };

        store.Save(entry);
        var filePath = Assert.Single(store.LoadAll()).FilePath;

        store.Delete(filePath);

        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Delete_OnAlreadyMissingFile_DoesNotThrow()
    {
        var store = new IconLibraryStore(_tempDir);

        var exception = Record.Exception(() => store.Delete(Path.Combine(_tempDir, "nonexistent.json")));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
