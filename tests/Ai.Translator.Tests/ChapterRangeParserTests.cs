using Ai.Translator.Core.Translation;

namespace Ai.Translator.Tests;

public sealed class ChapterRangeParserTests
{
    [Fact]
    public void Parse_Omitted_SelectsAllChapters()
    {
        var range = ChapterRangeParser.Parse(null, 3);

        Assert.Equal(1, range.From);
        Assert.Equal(3, range.To);
    }

    [Fact]
    public void Parse_SingleIndex()
    {
        var range = ChapterRangeParser.Parse("2", 3);

        Assert.Equal(2, range.From);
        Assert.Equal(2, range.To);
        Assert.True(range.Contains(2));
        Assert.False(range.Contains(1));
    }

    [Fact]
    public void Parse_InclusiveRange()
    {
        var range = ChapterRangeParser.Parse("2-4", 5);

        Assert.Equal(2, range.From);
        Assert.Equal(4, range.To);
    }

    [Fact]
    public void Parse_FromGreaterThanTo_ThrowsWithChapterCount()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ChapterRangeParser.Parse("4-1", 3));
        Assert.Contains("3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OutsideBook_ThrowsWithChapterCount()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ChapterRangeParser.Parse("5", 3));
        Assert.Contains("3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("outside", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
