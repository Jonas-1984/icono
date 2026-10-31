using Icono.Core;

namespace Icono.Tests;

public class PageRangeTests
{
    private static List<int>? Parse(string? text, int pageCount = 6) =>
        PageRange.TryParse(text, pageCount, out var pages, out _) ? pages : null;

    [Fact]
    public void SinglePagesAndRanges_AreReadAsZeroBasedAscendingPages()
    {
        Assert.Equal([0, 2, 3, 4], Parse("1,3-5"));
        Assert.Equal([1, 2, 3, 4, 5], Parse("2-"));
        Assert.Equal([0, 1, 2], Parse("-3"));
        Assert.Equal([0, 1, 2, 3, 4, 5], Parse("all"));
        Assert.Equal([2, 3], Parse(" 4 - 3 ; 3 "));
        Assert.Equal([0, 1], Parse("2,1,2,1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("1-9")]
    [InlineData("a")]
    [InlineData("1-b")]
    [InlineData("-")]
    [InlineData(",,")]
    public void WrongText_IsRefused_WithAMessage(string? text)
    {
        Assert.False(PageRange.TryParse(text, 6, out var pages, out var error));
        Assert.Empty(pages);
        Assert.NotEmpty(error);
    }
}
