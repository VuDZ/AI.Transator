using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Translation;

namespace Ai.Translator.Tests;

public sealed class ChapterChunkerTests
{
    private static readonly ModelProfile LargeWindow = new()
    {
        ContextWindowTokens = 400,
        ReservedOutputTokens = 50
    };

    [Fact]
    public void Chunk_WholeChapterFits_ReturnsSingleChunk()
    {
        var chapter = CreateChapter(
            "<p>One</p>",
            "<p>Two</p>",
            "<p>Three</p>");
        var chunker = new ChapterChunker(new LengthTokenEstimator());

        var chunks = chunker.Chunk(chapter, prefixTokenCount: 10, LargeWindow);

        var chunk = Assert.Single(chunks);
        Assert.Equal(chapter.BodyInnerHtml, chunk.SourceHtml);
        Assert.Equal(chapter.FilePath, chunk.ChapterFilePath);
    }

    [Fact]
    public void Chunk_RespectsTokenBudget()
    {
        var chapter = CreateChapter(
            "<p>AAAAAAAA</p>",
            "<p>BBBBBBBB</p>",
            "<p>CCCCCCCC</p>");
        var chunker = new ChapterChunker(new LengthTokenEstimator());
        var profile = new ModelProfile
        {
            ContextWindowTokens = 20,
            ReservedOutputTokens = 4
        };

        var chunks = chunker.Chunk(chapter, prefixTokenCount: 8, profile);

        Assert.True(chunks.Count >= 2, "Expected the chapter to be split.");
        var estimator = new LengthTokenEstimator();
        var budget = profile.ContextWindowTokens - 8 - profile.ReservedOutputTokens;
        foreach (var chunk in chunks)
        {
            Assert.True(estimator.Estimate(chunk.SourceHtml) <= budget, chunk.SourceHtml);
        }
    }

    [Fact]
    public void Chunk_PrefixAndBlockDoNotFit_Throws()
    {
        var chapter = CreateChapter("<p>Hello</p>");
        var chunker = new ChapterChunker(new LengthTokenEstimator());
        var profile = new ModelProfile
        {
            ContextWindowTokens = 10,
            ReservedOutputTokens = 8
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => chunker.Chunk(chapter, prefixTokenCount: 8, profile));
        Assert.Contains("glossary", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Chunk_SingleBlockLargerThanBudget_Throws()
    {
        var huge = new string('x', 400);
        var chapter = CreateChapter($"<p>{huge}</p>");
        var chunker = new ChapterChunker(new LengthTokenEstimator());
        var profile = new ModelProfile
        {
            ContextWindowTokens = 40,
            ReservedOutputTokens = 10
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => chunker.Chunk(chapter, prefixTokenCount: 5, profile));
        Assert.Equal(ChapterChunker.BudgetExceededMessage, ex.Message);
    }

    private static EpubChapter CreateChapter(params string[] fragments)
    {
        var body = string.Concat(fragments);
        return new EpubChapter
        {
            FilePath = "OEBPS/chapter1.xhtml",
            Xhtml = $"<html><body>{body}</body></html>",
            PlainText = "text",
            BodyInnerHtml = body,
            BlockFragments = fragments
        };
    }
}
