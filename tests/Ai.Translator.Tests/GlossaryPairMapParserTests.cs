using Ai.Translator.Core.Glossary;

namespace Ai.Translator.Tests;

public sealed class GlossaryPairMapParserTests
{
    private readonly GlossaryPairMapParser _parser = new();

    [Fact]
    public void Parse_SkipsCommentsAndEmptyLines()
    {
        var entries = _parser.Parse(
            """
            # heading

            OEBPS/a.xhtml = OEBPS/x.xhtml # trailing comment

            """);

        var entry = Assert.Single(entries);
        Assert.Equal("OEBPS/a.xhtml", entry.OriginalFrom);
        Assert.Equal("OEBPS/a.xhtml", entry.OriginalTo);
        Assert.Equal("OEBPS/x.xhtml", entry.TranslationFrom);
        Assert.Equal("OEBPS/x.xhtml", entry.TranslationTo);
    }

    [Fact]
    public void Parse_PointPair()
    {
        var entries = _parser.Parse("OEBPS/07-40k-Intro.xhtml = OEBPS/Text/Section0001.xhtml\n");
        var entry = Assert.Single(entries);
        Assert.Equal("OEBPS/07-40k-Intro.xhtml", entry.OriginalFrom);
        Assert.Equal("OEBPS/07-40k-Intro.xhtml", entry.OriginalTo);
        Assert.Equal("OEBPS/Text/Section0001.xhtml", entry.TranslationFrom);
        Assert.Equal("OEBPS/Text/Section0001.xhtml", entry.TranslationTo);
    }

    [Fact]
    public void Parse_RangeBlock()
    {
        var entries = _parser.Parse(
            "OEBPS/08-40k-Content.xhtml .. OEBPS/08-40k-Content-39.xhtml = OEBPS/Text/Section0003.xhtml .. OEBPS/Text/Section0042.xhtml\n");
        var entry = Assert.Single(entries);
        Assert.Equal("OEBPS/08-40k-Content.xhtml", entry.OriginalFrom);
        Assert.Equal("OEBPS/08-40k-Content-39.xhtml", entry.OriginalTo);
        Assert.Equal("OEBPS/Text/Section0003.xhtml", entry.TranslationFrom);
        Assert.Equal("OEBPS/Text/Section0042.xhtml", entry.TranslationTo);
    }

    [Fact]
    public void Parse_DawnOfFireExample_TwoPointsAndOneRange()
    {
        var path = FindRepoFile("docs", "examples", "dawn-of-fire-1.pairs.txt");
        var text = File.ReadAllText(path);
        var entries = _parser.Parse(text);

        Assert.Equal(3, entries.Count);
        Assert.Equal("OEBPS/07-40k-Intro.xhtml", entries[0].OriginalFrom);
        Assert.Equal(entries[0].OriginalFrom, entries[0].OriginalTo);
        Assert.Equal("OEBPS/Text/Section0001.xhtml", entries[0].TranslationFrom);
        Assert.Equal("OEBPS/07-40k-Intro-1.xhtml", entries[1].OriginalFrom);
        Assert.Equal(entries[1].OriginalFrom, entries[1].OriginalTo);
        Assert.Equal("OEBPS/08-40k-Content.xhtml", entries[2].OriginalFrom);
        Assert.Equal("OEBPS/08-40k-Content-39.xhtml", entries[2].OriginalTo);
        Assert.Equal("OEBPS/Text/Section0003.xhtml", entries[2].TranslationFrom);
        Assert.Equal("OEBPS/Text/Section0042.xhtml", entries[2].TranslationTo);
    }

    [Fact]
    public void Parse_LineWithoutEquals_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _parser.Parse("OEBPS/a.xhtml\n"));
        Assert.Contains("Invalid pair mapping line", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MixedPointAndRange_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _parser.Parse("OEBPS/a.xhtml .. OEBPS/b.xhtml = OEBPS/x.xhtml\n"));
        Assert.Contains("Both sides must be a single path or a range", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptyFile_ReturnsEmptyList()
    {
        Assert.Empty(_parser.Parse("# only a comment\n\n"));
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var parts = new string[relativeSegments.Length + 1];
            parts[0] = dir.FullName;
            relativeSegments.CopyTo(parts, 1);
            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not find " + Path.Combine(relativeSegments) + " from " + AppContext.BaseDirectory);
    }
}
