using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Options;
using Ai.Translator.Core.Translation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Ai.Translator.Tests;

public sealed class GlossaryExtractServiceTests
{
    [Fact]
    public async Task ExtractAsync_MockReturningTwoEntries_WritesValidMarkdown()
    {
        var root = NewRoot();
        try
        {
            var originalPath = Path.Combine(root, "original.epub");
            var translationPath = Path.Combine(root, "translation.epub");
            var outPath = Path.Combine(root, "extracted.md");
            await File.WriteAllTextAsync(originalPath, "placeholder");
            await File.WriteAllTextAsync(translationPath, "placeholder");

            var llm = CreateLlm(
                """
                Here are proposed entries.

                ## Librarian
                - ru: Библиарий
                - type: title
                - aliases: Librarians

                ## Adeptus Astartes
                - ru: Адептус Астартес
                - type: organization
                """);

            var originalEpub = CreateEpubMock(
                originalPath,
                Chapter("OEBPS/chapter1.xhtml", "The Librarian hailed the Adeptus Astartes."));
            var translationEpub = CreateEpubMock(
                translationPath,
                Chapter("OEBPS/chapter1.xhtml", "Библиарий приветствовал Адептус Астартес."));
            var epub = CombineEpub(originalEpub, translationEpub);

            var service = CreateService(llm.Object, epub.Object);
            await service.ExtractAsync(
                originalPath,
                translationPath,
                outPath,
                mergeIntoPath: null,
                model: "extract-model",
                CancellationToken.None);

            Assert.True(File.Exists(outPath));
            var parsed = new GlossaryParser().Parse(await File.ReadAllTextAsync(outPath));
            Assert.Equal(2, parsed.Entries.Count);
            Assert.Equal("Librarian", parsed.Entries[0].English);
            Assert.Equal("Библиарий", parsed.Entries[0].Ru);
            Assert.Equal("Adeptus Astartes", parsed.Entries[1].English);
            Assert.Equal("Адептус Астартес", parsed.Entries[1].Ru);

            llm.Verify(
                x => x.CompleteAsync(
                    It.Is<LlmRequest>(req =>
                        req.Model == "extract-model"
                        && req.StablePrefix.Contains("glossary extractor", StringComparison.OrdinalIgnoreCase)
                        && !req.StablePrefix.Contains("literary translator", StringComparison.OrdinalIgnoreCase)
                        && req.VariableContent.Contains("The Librarian hailed", StringComparison.Ordinal)
                        && req.VariableContent.Contains("Библиарий приветствовал", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExtractAsync_MergeInto_PreservesRuAppendsAliasAndAddsEntry()
    {
        var root = NewRoot();
        try
        {
            var originalPath = Path.Combine(root, "original.epub");
            var translationPath = Path.Combine(root, "translation.epub");
            var corpusPath = Path.Combine(root, "corpus.md");
            var outPath = Path.Combine(root, "merged.md");
            await File.WriteAllTextAsync(originalPath, "placeholder");
            await File.WriteAllTextAsync(translationPath, "placeholder");
            await File.WriteAllTextAsync(
                corpusPath,
                """
                # Warhammer 40k

                Edition notes stay.

                ## Librarian
                - ru: Библиарий
                - type: title
                """);

            var llm = CreateLlm(
                """
                ## Librarian
                - ru: Библиотекарь
                - type: title
                - aliases: Librarians

                ## Adeptus Astartes
                - ru: Адептус Астартес
                - type: organization
                """);

            var epub = CombineEpub(
                CreateEpubMock(originalPath, Chapter("OEBPS/ch1.xhtml", "Librarian")),
                CreateEpubMock(translationPath, Chapter("OEBPS/ch1.xhtml", "Библиарий")));

            await CreateService(llm.Object, epub.Object).ExtractAsync(
                originalPath,
                translationPath,
                outPath,
                corpusPath,
                model: null,
                CancellationToken.None);

            var parsed = new GlossaryParser().Parse(await File.ReadAllTextAsync(outPath));
            Assert.Equal("Warhammer 40k", parsed.Title);
            Assert.Equal("Edition notes stay.", parsed.Preamble);
            Assert.Equal(2, parsed.Entries.Count);
            Assert.Equal("Библиарий", parsed.Entries[0].Ru);
            Assert.Equal(["Librarians"], parsed.Entries[0].Aliases);
            Assert.Equal("Adeptus Astartes", parsed.Entries[1].English);
            Assert.DoesNotContain("Библиотекарь", await File.ReadAllTextAsync(corpusPath), StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExtractAsync_DifferentSpineLengths_DoesNotThrow()
    {
        var logger = new Mock<ILogger<GlossaryExtractor>>();
        var llm = CreateLlm(
            """
            ## Librarian
            - ru: Библиарий
            - type: title
            """);

        var original = new EpubBookModel
        {
            Chapters =
            [
                Chapter("OEBPS/ch1.xhtml", "One"),
                Chapter("OEBPS/ch2.xhtml", "Two leftover")
            ]
        };
        var translation = new EpubBookModel
        {
            Chapters = [Chapter("OEBPS/tr1.xhtml", "Один")]
        };

        var extractor = CreateExtractor(llm.Object, logger.Object);
        var document = await extractor.ExtractAsync(
            original,
            translation,
            existingCorpus: null,
            model: "test-model",
            CancellationToken.None);

        Assert.Equal("Librarian", Assert.Single(document.Entries).English);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Reading order lengths differ", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("OEBPS/ch2.xhtml", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_RejectsPdfOriginal()
    {
        var service = CreateService(
            new Mock<ILlmProvider>(MockBehavior.Strict).Object,
            new Mock<IEpubBookService>(MockBehavior.Strict).Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtractAsync("book.pdf", "book.epub", "out.md", null, null, CancellationToken.None));
        Assert.Equal(InputPathGuard.PdfRejectedMessage, ex.Message);
    }

    [Fact]
    public async Task ExtractAsync_RejectsPdfTranslation()
    {
        var service = CreateService(
            new Mock<ILlmProvider>(MockBehavior.Strict).Object,
            new Mock<IEpubBookService>(MockBehavior.Strict).Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtractAsync("book.epub", "book.pdf", "out.md", null, null, CancellationToken.None));
        Assert.Equal(InputPathGuard.PdfRejectedMessage, ex.Message);
    }

    [Fact]
    public void Extractor_DoesNotDependOnBookTranslationService()
    {
        var constructor = typeof(GlossaryExtractor).GetConstructors().Single();
        Assert.DoesNotContain(
            constructor.GetParameters(),
            p => p.ParameterType == typeof(IBookTranslationService));

        var translationConstructor = typeof(Ai.Translator.Core.Translation.BookTranslationService)
            .GetConstructors()
            .Single();
        Assert.DoesNotContain(
            translationConstructor.GetParameters(),
            p => p.ParameterType == typeof(IGlossaryExtractor)
                || p.ParameterType == typeof(IGlossaryExtractService));
    }

    private static GlossaryExtractService CreateService(ILlmProvider llm, IEpubBookService epub)
    {
        return new GlossaryExtractService(
            epub,
            new GlossaryParser(),
            new GlossaryWriter(),
            CreateExtractor(llm));
    }

    private static GlossaryExtractor CreateExtractor(
        ILlmProvider llm,
        ILogger<GlossaryExtractor>? logger = null)
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
            }),
            logger);
    }

    private static Mock<ILlmProvider> CreateLlm(string content)
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = content, FinishReason = "stop" });
        return llm;
    }

    private static Mock<IEpubBookService> CreateEpubMock(string path, params EpubChapter[] chapters)
    {
        var epub = new Mock<IEpubBookService>(MockBehavior.Strict);
        epub.Setup(x => x.OpenAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EpubBookModel { Chapters = chapters });
        return epub;
    }

    private static Mock<IEpubBookService> CombineEpub(
        Mock<IEpubBookService> first,
        Mock<IEpubBookService> second)
    {
        var epub = new Mock<IEpubBookService>(MockBehavior.Strict);
        epub.Setup(x => x.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken ct) =>
            {
                if (path.Contains("original", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("original.epub", StringComparison.OrdinalIgnoreCase))
                {
                    return first.Object.OpenAsync(path, ct);
                }

                return second.Object.OpenAsync(path, ct);
            });
        return epub;
    }

    private static EpubChapter Chapter(string path, string plainText) =>
        new()
        {
            FilePath = path,
            Xhtml = "<html><body><p>" + plainText + "</p></body></html>",
            PlainText = plainText,
            BodyInnerHtml = "<p>" + plainText + "</p>"
        };

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
