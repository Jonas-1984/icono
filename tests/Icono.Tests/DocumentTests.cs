using Icono.Core;

namespace Icono.Tests;

public class DocumentTests
{
    [Fact]
    public void NewDocument_DefaultsToPreviewSize()
    {
        var document = new Document();

        Assert.Equal(Document.PreviewSize, document.Width);
        Assert.Equal(Document.PreviewSize, document.Height);
    }

    [Fact]
    public void ExportSizes_MatchesFixedRequirement()
    {
        Assert.Equal(new[] { 64, 128, 256, 512 }, Document.ExportSizes);
    }
}
