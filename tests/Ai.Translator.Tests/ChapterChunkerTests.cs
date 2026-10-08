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

    [Theory]
    [InlineData(0, 0, 85000)]
    [InlineData(2, 0, 64000)]
    [InlineData(2, 8192, 59904)]
    public void Chunk_LargeInputAndIndependentOutput_PreservesBlocks(double multiplier, int reasoningReserve, int expectedBudget)
    {
        var fragments = Enumerable.Range(0, 10).Select(i => $"<p>{i}" + new string('x', 40000) + "</p>").ToArray();
        var chapter = CreateChapter(fragments);
        var estimator = new LengthTokenEstimator();
        var profile = new ModelProfile
        {
            ContextWindowTokens = 1000000,
            MaxInputTokens = 90000,
            ReservedOutputTokens = 128000,
            TranslationOutputTokenMultiplier = multiplier,
            ReasoningTokenReserve = reasoningReserve
        };
        Assert.Equal(expectedBudget, profile.GetSourceBudget(5000));
        var chunks = new ChapterChunker(estimator).Chunk(chapter, 5000, profile);
        Assert.True(chunks.Count > 1);
        Assert.Equal(chapter.BodyInnerHtml, string.Concat(chunks.Select(c => c.SourceHtml)));
        foreach (var chunk in chunks)
        {
            Assert.True(estimator.Estimate(chunk.SourceHtml) <= expectedBudget);
        }
    }

    [Fact]
    public void Chunk_TwoHundredKilobyteChapter_FitsWithSeparateOutput()
    {
        var chapter = CreateChapter("<p>" + new string('x', 200000) + "</p>");
        var profile = new ModelProfile
        {
            ContextWindowTokens = 1000000,
            MaxInputTokens = 90000,
            ReservedOutputTokens = 128000,
            TranslationOutputTokenMultiplier = 2,
            ReasoningTokenReserve = 8192
        };
        Assert.Equal(chapter.BodyInnerHtml, Assert.Single(new ChapterChunker(new LengthTokenEstimator()).Chunk(chapter, 5000, profile)).SourceHtml);
    }

    [Fact]
    public void Chunk_FullContextStillLimitsInputAndOutput()
    {
        var profile = new ModelProfile { ContextWindowTokens = 100000, MaxInputTokens = 90000, ReservedOutputTokens = 30000 };
        Assert.Equal(65000, profile.GetSourceBudget(5000));
    }

    [Fact]
    public void Chunk_SubTokenFragments_DoesNotUnderestimateCombinedHtml()
    {
        var chapter = CreateChapter("xxx", "xxx", "xxx", "xxx", "xxx", "xxx");
        var profile = new ModelProfile { ContextWindowTokens = 5, ReservedOutputTokens = 3, MaxInputTokens = 5 };
        var chunks = new ChapterChunker(new LengthTokenEstimator()).Chunk(chapter, 0, profile);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(new LengthTokenEstimator().Estimate(chunk.SourceHtml) <= 2));
        Assert.Equal(chapter.BodyInnerHtml, string.Concat(chunks.Select(c => c.SourceHtml)));
    }

    [Theory]
    [InlineData(0, 0, 1, 0)]
    [InlineData(10, 0, 1, 0)]
    [InlineData(10, 1, -1, 0)]
    [InlineData(10, 1, 1, 1)]
    [InlineData(10, 1, 1, -1)]
    public void Chunk_InvalidTokenBudgets_Throws(int context, int output, double multiplier, int reasoningReserve)
    {
        var profile = new ModelProfile
        {
            ContextWindowTokens = context,
            ReservedOutputTokens = output,
            TranslationOutputTokenMultiplier = multiplier,
            ReasoningTokenReserve = reasoningReserve
        };
        Assert.Throws<InvalidOperationException>(() => new ChapterChunker(new LengthTokenEstimator()).Chunk(CreateChapter("<p>text</p>"), 0, profile));
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
