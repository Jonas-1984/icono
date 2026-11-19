using Icono.Core;
using Icono.IO;

namespace Icono.Tests;

public class DocumentRecoveryTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "icono-recovery-" + Guid.NewGuid().ToString("N"), "icono-recovery.icn");

    private static Document Sample()
    {
        var document = new Document { Width = 500, Height = 400 };
        var layer = new Layer { Name = "Plan" };
        layer.Shapes.Add(new Shape { Kind = ShapeKind.Rectangle, X = 10, Y = 20, Width = 80, Height = 40, Name = "Box" });
        document.Layers.Add(layer);
        return document;
    }

    private static void Clean(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ADrawing_IsKept_AndComesBackWithItsTitle()
    {
        var path = TempPath();
        try
        {
            Assert.True(DocumentRecovery.Save(Sample(), "Flat 3", path));
            Assert.True(DocumentRecovery.Exists(path));

            var found = DocumentRecovery.TryLoad(path);

            Assert.NotNull(found);
            Assert.Equal("Flat 3", found!.Value.Name);
            var restored = new Document();
            found.Value.File.ApplyTo(restored);
            Assert.Equal(500, restored.Width);
            var shape = Assert.Single(Assert.Single(restored.Layers).Shapes);
            Assert.Equal("Box", shape.Name);
        }
        finally
        {
            Clean(path);
        }
    }

    [Fact]
    public void ANewerCopy_ReplacesTheOlderOne()
    {
        var path = TempPath();
        try
        {
            DocumentRecovery.Save(Sample(), "First", path);
            var second = Sample();
            second.Width = 900;
            DocumentRecovery.Save(second, "Second", path);

            var found = DocumentRecovery.TryLoad(path)!.Value;

            Assert.Equal("Second", found.Name);
            Assert.Equal(900, found.File.Width);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Clean(path);
        }
    }

    [Fact]
    public void NoCopy_GivesNothing()
    {
        Assert.Null(DocumentRecovery.TryLoad(TempPath()));
        DocumentRecovery.Delete(TempPath()); // deleting what is not there is no error
    }

    [Fact]
    public void ABrokenCopy_IsNotOffered_AndIsDeleted()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not a drawing");

            Assert.Null(DocumentRecovery.TryLoad(path));

            Assert.False(File.Exists(path));
        }
        finally
        {
            Clean(path);
        }
    }

    [Fact]
    public void Delete_RemovesTheCopyAndItsTitle()
    {
        var path = TempPath();
        try
        {
            DocumentRecovery.Save(Sample(), "Plan", path);

            DocumentRecovery.Delete(path);

            Assert.False(DocumentRecovery.Exists(path));
            Assert.False(File.Exists(path + ".name"));
            Assert.Null(DocumentRecovery.TryLoad(path));
        }
        finally
        {
            Clean(path);
        }
    }

    [Fact]
    public void ACopyWithoutATitle_IsCalledUntitled()
    {
        var path = TempPath();
        try
        {
            DocumentRecovery.Save(Sample(), "Plan", path);
            File.Delete(path + ".name");

            Assert.Equal("Untitled", DocumentRecovery.TryLoad(path)!.Value.Name);
        }
        finally
        {
            Clean(path);
        }
    }
}
