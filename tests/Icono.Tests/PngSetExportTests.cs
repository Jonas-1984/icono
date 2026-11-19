using System.IO.Compression;
using Icono.Core;
using Icono.Rendering;

namespace Icono.Tests;

[Collection("Scale")]
public class PngSetExportTests
{
    private static Document TwoPages()
    {
        var document = new Document { Width = 300, Height = 200, PageCount = 2 };
        document.Layers.Add(new Layer { Name = "L" });
        return document;
    }

    private static List<ZipArchiveEntry> Entries(byte[] zip, out ZipArchive archive)
    {
        archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.ToList();
    }

    [Fact]
    public void OnePage_GivesSixPictures_NamedBySize()
    {
        var zip = DocumentExporter.RenderPngSetZip(TwoPages(), "plan", includeBackground: true, page: 0);

        var entries = Entries(zip, out var archive);
        using (archive)
        {
            Assert.Equal(["plan_16.png", "plan_32.png", "plan_64.png", "plan_128.png", "plan_256.png", "plan_512.png"], entries.Select(e => e.Name));
            foreach (var entry in entries)
            {
                using var stream = entry.Open();
                var header = new byte[4];
                Assert.Equal(4, stream.Read(header, 0, 4));
                Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], header); // a real PNG
            }
        }
    }

    [Fact]
    public void AllPages_GiveSixPicturesPerPage()
    {
        var zip = DocumentExporter.RenderPngSetZip(TwoPages(), "plan", includeBackground: false);

        var entries = Entries(zip, out var archive);
        using (archive)
        {
            Assert.Equal(12, entries.Count);
            Assert.Contains(entries, e => e.Name == "plan_page1_16.png");
            Assert.Contains(entries, e => e.Name == "plan_page2_512.png");
        }
    }
}
