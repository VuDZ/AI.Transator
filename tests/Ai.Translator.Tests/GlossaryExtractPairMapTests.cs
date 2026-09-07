using System.Text;
using System.CommandLine;
using Ai.Translator.Cli;
using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Options;
using Ai.Translator.Core.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Ai.Translator.Tests;

public sealed class GlossaryExtractPairMapTests
{
    [Fact]
    public async Task ExtractAsync_PointPair_SendsMappedChaptersOnly()
    {
        var llm = CreateLlm();
        var requests = CaptureRequests(llm);
        var extractor = CreateExtractor(llm.Object);

        await extractor.ExtractAsync(
            Book(
                Chapter("OEBPS/A.xhtml", "Alpha unique"),
                Chapter("OEBPS/B.xhtml", "Bravo unique"),
                Chapter("OEBPS/C.xhtml", "Charlie unique")),
            Book(
                Chapter("OEBPS/X.xhtml", "Икс уникальный"),
                Chapter("OEBPS/Y.xhtml", "Игрек уникальный"),
                Chapter("OEBPS/Z.xhtml", "Зет уникальный")),
            existingCorpus: null,
            model: "test-model",
            pairMap: [Point("OEBPS/A.xhtml", "OEBPS/Y.xhtml")],
            maxPairs: null,
            CancellationToken.None);

        var variable = Assert.Single(requests).VariableContent;
        Assert.Contains("Alpha unique", variable, StringComparison.Ordinal);
        Assert.Contains("Игрек уникальный", variable, StringComparison.Ordinal);
        Assert.DoesNotContain("Bravo unique", variable, StringComparison.Ordinal);
        Assert.DoesNotContain("Charlie unique", variable, StringComparison.Ordinal);
        Assert.DoesNotContain("Икс уникальный", variable, StringComparison.Ordinal);
        Assert.DoesNotContain("Зет уникальный", variable, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_Block_ExpandsInKeptOrder()
    {
        var llm = CreateLlm();
        var requests = CaptureRequests(llm);
        var extractor = CreateExtractor(llm.Object);

        await extractor.ExtractAsync(
            Book(
                Chapter("OEBPS/A.xhtml", "Alpha unique"),
                Chapter("OEBPS/B.xhtml", "Bravo unique")),
            Book(
                Chapter("OEBPS/X.xhtml", "Икс уникальный"),
                Chapter("OEBPS/Y.xhtml", "Игрек уникальный")),
            existingCorpus: null,
            model: "test-model",
            pairMap:
            [
                new PairMapEntry
                {
                    OriginalFrom = "OEBPS/A.xhtml",
                    OriginalTo = "OEBPS/B.xhtml",
                    TranslationFrom = "OEBPS/X.xhtml",
                    TranslationTo = "OEBPS/Y.xhtml"
                }
            ],
            maxPairs: null,
            CancellationToken.None);

        Assert.Equal(2, requests.Count);
        Assert.Contains("Alpha unique", requests[0].VariableContent, StringComparison.Ordinal);
        Assert.Contains("Икс уникальный", requests[0].VariableContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Bravo unique", requests[0].VariableContent, StringComparison.Ordinal);
        Assert.Contains("Bravo unique", requests[1].VariableContent, StringComparison.Ordinal);
        Assert.Contains("Игрек уникальный", requests[1].VariableContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha unique", requests[1].VariableContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_MissingPath_DoesNotCallLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var extractor = CreateExtractor(llm.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(
                Book(Chapter("OEBPS/A.xhtml", "Alpha")),
                Book(Chapter("OEBPS/X.xhtml", "Икс")),
                existingCorpus: null,
                model: "test-model",
                pairMap: [Point("OEBPS/missing.xhtml", "OEBPS/X.xhtml")],
                maxPairs: null,
                CancellationToken.None));

        Assert.Contains("OEBPS/missing.xhtml", ex.Message, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_RangeLengthMismatch_DoesNotCallLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var extractor = CreateExtractor(llm.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(
                Book(
                    Chapter("OEBPS/A.xhtml", "Alpha"),
                    Chapter("OEBPS/B.xhtml", "Bravo")),
                Book(
                    Chapter("OEBPS/X.xhtml", "Икс"),
                    Chapter("OEBPS/Y.xhtml", "Игрек"),
                    Chapter("OEBPS/Z.xhtml", "Зет")),
                existingCorpus: null,
                model: "test-model",
                pairMap:
                [
                    new PairMapEntry
                    {
                        OriginalFrom = "OEBPS/A.xhtml",
                        OriginalTo = "OEBPS/B.xhtml",
                        TranslationFrom = "OEBPS/X.xhtml",
                        TranslationTo = "OEBPS/Z.xhtml"
                    }
                ],
                maxPairs: null,
                CancellationToken.None));

        Assert.Contains("2 vs 3", ex.Message, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_MaxPairs_TruncatesAfterExpand()
    {
        var llm = CreateLlm();
        var requests = CaptureRequests(llm);
        var extractor = CreateExtractor(llm.Object);

        await extractor.ExtractAsync(
            Book(
                Chapter("OEBPS/A.xhtml", "Alpha unique"),
                Chapter("OEBPS/B.xhtml", "Bravo unique"),
                Chapter("OEBPS/C.xhtml", "Charlie unique")),
            Book(
                Chapter("OEBPS/X.xhtml", "Икс уникальный"),
                Chapter("OEBPS/Y.xhtml", "Игрек уникальный"),
                Chapter("OEBPS/Z.xhtml", "Зет уникальный")),
            existingCorpus: null,
            model: "test-model",
            pairMap:
            [
                Point("OEBPS/A.xhtml", "OEBPS/X.xhtml"),
                Point("OEBPS/B.xhtml", "OEBPS/Y.xhtml"),
                Point("OEBPS/C.xhtml", "OEBPS/Z.xhtml")
            ],
            maxPairs: 2,
            CancellationToken.None);

        Assert.Equal(2, requests.Count);
        Assert.Contains("Alpha unique", requests[0].VariableContent, StringComparison.Ordinal);
        Assert.Contains("Bravo unique", requests[1].VariableContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Charlie unique", requests[0].VariableContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Charlie unique", requests[1].VariableContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Зет уникальный", requests[0].VariableContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Зет уникальный", requests[1].VariableContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_MaxPairsWithoutMap_DoesNotCallLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var extractor = CreateExtractor(llm.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(
                Book(Chapter("OEBPS/A.xhtml", "Alpha")),
                Book(Chapter("OEBPS/X.xhtml", "Икс")),
                existingCorpus: null,
                model: "test-model",
                pairMap: null,
                maxPairs: 2,
                CancellationToken.None));

        Assert.Equal(GlossaryExtractArguments.MaxPairsRequiresPairsMessage, ex.Message);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExtractAsync_MaxPairsNotPositive_DoesNotCallLlm(int maxPairs)
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var extractor = CreateExtractor(llm.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(
                Book(Chapter("OEBPS/A.xhtml", "Alpha")),
                Book(Chapter("OEBPS/X.xhtml", "Икс")),
                existingCorpus: null,
                model: "test-model",
                pairMap: [Point("OEBPS/A.xhtml", "OEBPS/X.xhtml")],
                maxPairs,
                CancellationToken.None));

        Assert.Equal(GlossaryExtractArguments.MaxPairsMustBePositiveMessage, ex.Message);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_PathSlashAndCase_MatchKeptChapter()
    {
        var llm = CreateLlm();
        var requests = CaptureRequests(llm);
        var extractor = CreateExtractor(llm.Object);

        await extractor.ExtractAsync(
            Book(Chapter("OEBPS/A.xhtml", "Alpha unique")),
            Book(Chapter("OEBPS/Y.xhtml", "Игрек уникальный")),
            existingCorpus: null,
            model: "test-model",
            pairMap: [Point("oebps\\a.xhtml", "OEBPS/Y.xhtml")],
            maxPairs: null,
            CancellationToken.None);

        var variable = Assert.Single(requests).VariableContent;
        Assert.Contains("Alpha unique", variable, StringComparison.Ordinal);
        Assert.Contains("Игрек уникальный", variable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_DuplicatePath_DoesNotCallLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var extractor = CreateExtractor(llm.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(
                Book(
                    Chapter("OEBPS/A.xhtml", "Alpha"),
                    Chapter("OEBPS/B.xhtml", "Bravo")),
                Book(
                    Chapter("OEBPS/X.xhtml", "Икс"),
                    Chapter("OEBPS/Y.xhtml", "Игрек")),
                existingCorpus: null,
                model: "test-model",
                pairMap:
                [
                    Point("OEBPS/A.xhtml", "OEBPS/X.xhtml"),
                    Point("OEBPS/A.xhtml", "OEBPS/Y.xhtml")
                ],
                maxPairs: null,
                CancellationToken.None));

        Assert.Contains("OEBPS/A.xhtml", ex.Message, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void ExtractArguments_PairsConflicts()
    {
        Assert.True(GlossaryExtractArguments.HasListPairsPairsConflict(true, "pairs.txt"));
        Assert.False(GlossaryExtractArguments.HasListPairsPairsConflict(true, null));
        Assert.True(GlossaryExtractArguments.HasMaxPairsWithoutPairs(2, null));
        Assert.False(GlossaryExtractArguments.HasMaxPairsWithoutPairs(2, "pairs.txt"));
        Assert.True(GlossaryExtractArguments.HasInvalidMaxPairs(0));
        Assert.True(GlossaryExtractArguments.HasInvalidMaxPairs(-3));
        Assert.False(GlossaryExtractArguments.HasInvalidMaxPairs(1));
        Assert.False(GlossaryExtractArguments.HasInvalidMaxPairs(null));
        Assert.True(GlossaryExtractArguments.HasPairsWithoutOut("pairs.txt", null));
        Assert.False(GlossaryExtractArguments.HasPairsWithoutOut("pairs.txt", "out.md"));
    }

    [Fact]
    public async Task ListPairs_WithPairs_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--list-pairs",
            "--pairs", "pairs.txt");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.ListPairsPairsConflictMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MaxPairs_WithoutPairs_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--out", "out.md",
            "--max-pairs", "2");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.MaxPairsRequiresPairsMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Pairs_WithoutOut_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--pairs", "pairs.txt");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.PairsRequiresOutMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MaxPairs_Zero_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--out", "out.md",
            "--pairs", "pairs.txt",
            "--max-pairs", "0");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.MaxPairsMustBePositiveMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> InvokeExtractAsync(
        ILlmProvider llm,
        params string[] extractArgs)
    {
        const string json =
            """
            {
              "Llm": {
                "BaseUrl": "http://127.0.0.1:9/v1",
                "Model": "test-model",
                "ApiKey": "test-key",
                "ContextWindowTokens": 8000,
                "ReservedOutputTokens": 500
              }
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var services = new ServiceCollection();
        services.AddTranslator(configuration);
        services.AddSingleton(llm);
        await using var provider = services.BuildServiceProvider();

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var args = new string[extractArgs.Length + 2];
        args[0] = "glossary";
        args[1] = "extract";
        extractArgs.CopyTo(args, 2);

        var parseResult = CommandTree.Create(provider).Parse(args);
        parseResult.InvocationConfiguration.Output = stdout;
        parseResult.InvocationConfiguration.Error = stderr;
        var exit = await parseResult.InvokeAsync();
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private static Mock<ILlmProvider> CreateLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse
            {
                Content =
                    """
                    ## Librarian
                    - ru: Библиарий
                    - type: title
                    """,
                FinishReason = "stop"
            });
        return llm;
    }

    private static List<LlmRequest> CaptureRequests(Mock<ILlmProvider> llm)
    {
        var requests = new List<LlmRequest>();
        llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LlmRequest, CancellationToken>((req, _) => requests.Add(req))
            .ReturnsAsync(new LlmResponse
            {
                Content =
                    """
                    ## Librarian
                    - ru: Библиарий
                    - type: title
                    """,
                FinishReason = "stop"
            });
        return requests;
    }

    private static GlossaryExtractor CreateExtractor(ILlmProvider llm)
    {
        return new GlossaryExtractor(
            llm,
            new GlossaryParser(),
            new GlossaryWriter(),
            new GlossaryMerger(),
            new LengthTokenEstimator(),
            new ExtractRulesLoader(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            Options.Create(new TranslatorOptions { Temperature = 0.1 }),
            Options.Create(new LlmOptions
            {
                Model = "test-model",
                ContextWindowTokens = 8000,
                ReservedOutputTokens = 500
            }));
    }

    private static EpubBookModel Book(params EpubChapter[] chapters) =>
        new() { Chapters = chapters };

    private static EpubChapter Chapter(string path, string plainText) =>
        new()
        {
            FilePath = path,
            Xhtml = "<html><body><p>" + plainText + "</p></body></html>",
            PlainText = plainText,
            BodyInnerHtml = "<p>" + plainText + "</p>"
        };

    private static PairMapEntry Point(string original, string translation) =>
        new()
        {
            OriginalFrom = original,
            OriginalTo = original,
            TranslationFrom = translation,
            TranslationTo = translation
        };
}
