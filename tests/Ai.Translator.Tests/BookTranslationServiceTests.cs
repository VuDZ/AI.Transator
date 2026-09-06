using System.IO.Compression;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Epub;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Llm;
using Ai.Translator.Core.Options;
using Ai.Translator.Core.Translation;
using Microsoft.Extensions.Options;
using Moq;

namespace Ai.Translator.Tests;

public sealed class BookTranslationServiceTests
{
    [Fact]
    public async Task RunAsync_TwoChapters_WritesTranslatedEpubAndKeepsAssets()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(
                root,
                "<p>Alpha Librarian</p>",
                "<p>Beta Librarian</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var llm = CreateMappingLlm(req =>
            {
                if (req.VariableContent.Contains("Alpha", StringComparison.Ordinal))
                {
                    return Ok("<p>Альфа Библиарий</p>");
                }

                if (req.VariableContent.Contains("Beta", StringComparison.Ordinal))
                {
                    return Ok("<p>Бета Библиарий</p>");
                }

                return Ok("<p>Прочее</p>");
            });

            var result = await CreateService(llm.Object).RunAsync(
                Job(input, glossary, output, workDir),
                CancellationToken.None);

            Assert.False(result.HasFailures);
            var book = await new EpubBookService().OpenAsync(output, CancellationToken.None);
            Assert.Equal(2, book.Chapters.Count);
            Assert.Contains("Альфа", book.Chapters[0].PlainText, StringComparison.Ordinal);
            Assert.Contains("Бета", book.Chapters[1].PlainText, StringComparison.Ordinal);

            using var zip = ZipFile.OpenRead(output);
            Assert.NotNull(zip.GetEntry("OEBPS/styles/style.css"));
            Assert.NotNull(zip.GetEntry("OEBPS/images/pixel.png"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_Chapters2_TranslatesOnlySecondOfThree()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(
                root,
                "<p>Alpha</p>",
                "<p>Beta</p>",
                "<p>Gamma</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var llm = CreateMappingLlm(req =>
            {
                Assert.Contains("Beta", req.VariableContent, StringComparison.Ordinal);
                Assert.DoesNotContain("Alpha", req.VariableContent, StringComparison.Ordinal);
                Assert.DoesNotContain("Gamma", req.VariableContent, StringComparison.Ordinal);
                return Ok("<p>Бета</p>");
            });

            var result = await CreateService(llm.Object).RunAsync(
                Job(input, glossary, output, workDir, chapters: "2"),
                CancellationToken.None);

            Assert.False(result.HasFailures);
            llm.Verify(
                x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);

            var book = await new EpubBookService().OpenAsync(output, CancellationToken.None);
            Assert.Equal(3, book.Chapters.Count);
            Assert.Contains("Alpha", book.Chapters[0].PlainText, StringComparison.Ordinal);
            Assert.Contains("Бета", book.Chapters[1].PlainText, StringComparison.Ordinal);
            Assert.Contains("Gamma", book.Chapters[2].PlainText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_TruncatedOnce_RetriesLlm()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(root, "<p>RetryMe</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var calls = 0;
            var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
            llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        return new LlmResponse
                        {
                            Content = "<p>Обрыв",
                            FinishReason = "length",
                            PromptTokens = 8
                        };
                    }

                    return Ok("<p>Повтор</p>");
                });

            var result = await CreateService(llm.Object, maxRetries: 3).RunAsync(
                Job(input, glossary, output, workDir),
                CancellationToken.None);

            Assert.False(result.HasFailures);
            Assert.Equal(2, calls);
            var book = await new EpubBookService().OpenAsync(output, CancellationToken.None);
            Assert.Contains("Повтор", Assert.Single(book.Chapters).PlainText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_OneChunkExhaustsRetries_ContinuesAndReportsFailure()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(
                root,
                "<p>FAILME</p>",
                "<p>KeepMe</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var llm = CreateMappingLlm(req =>
            {
                if (req.VariableContent.Contains("FAILME", StringComparison.Ordinal))
                {
                    return new LlmResponse { Content = "", FinishReason = "stop", PromptTokens = 1 };
                }

                return Ok("<p>Сохранено</p>");
            });

            var result = await CreateService(llm.Object, maxRetries: 2).RunAsync(
                Job(input, glossary, output, workDir),
                CancellationToken.None);

            Assert.True(result.HasFailures);
            Assert.Equal(1, result.FailedChunkCount);
            var book = await new EpubBookService().OpenAsync(output, CancellationToken.None);
            Assert.Contains("FAILME", book.Chapters[0].PlainText, StringComparison.Ordinal);
            Assert.Contains("Сохранено", book.Chapters[1].PlainText, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(workDir, "chunks", "0001-0000.error.txt")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_Resume_DoesNotCallLlmForDoneChunks()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(
                root,
                "<p>Alpha</p>",
                "<p>Beta</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var calls = 0;
            var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
            llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LlmRequest req, CancellationToken _) =>
                {
                    calls++;
                    if (req.VariableContent.Contains("Alpha", StringComparison.Ordinal))
                    {
                        return Ok("<p>Альфа</p>");
                    }

                    return Ok("<p>Бета</p>");
                });

            var service = CreateService(llm.Object);
            var job = Job(input, glossary, output, workDir);
            Assert.False((await service.RunAsync(job, CancellationToken.None)).HasFailures);
            var firstPassCalls = calls;

            var resumeJob = new TranslationJob
            {
                InputPath = input,
                GlossaryPath = glossary,
                OutputPath = output,
                WorkDir = workDir,
                Resume = true
            };
            Assert.False((await service.RunAsync(resumeJob, CancellationToken.None)).HasFailures);
            Assert.Equal(firstPassCalls, calls);
            Assert.Equal(2, firstPassCalls);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_ResumeWithChangedGlossary_Throws()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(root, "<p>Alpha</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var llm = CreateMappingLlm(_ => Ok("<p>Альфа</p>"));
            var service = CreateService(llm.Object);
            await service.RunAsync(Job(input, glossary, output, workDir), CancellationToken.None);

            await File.WriteAllTextAsync(
                glossary,
                """
                # Test

                Keep names.

                ## Librarian
                - ru: Библиарий
                - type: title

                ## Inquisitor
                - ru: Инквизитор
                - type: title
                """);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RunAsync(
                    new TranslationJob
                    {
                        InputPath = input,
                        GlossaryPath = glossary,
                        OutputPath = output,
                        WorkDir = workDir,
                        Resume = true
                    },
                    CancellationToken.None));
            Assert.Contains("prefix hash", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_PrefixOmitsChapterText_AndCandidatesStayOutOfPrefix()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(root, "<p>UNIQUE_CHAPTER_PHRASE Eisenhorn Librarian</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            string? prefix = null;
            var promptRoot = Path.Combine(root, "prompt-root");
            Directory.CreateDirectory(Path.Combine(promptRoot, "prompts"));
            await File.WriteAllTextAsync(
                Path.Combine(promptRoot, "prompts", "translate-system.md"),
                "CANON_LAYER_ONE_FROM_FILE\n");
            var llm = CreateMappingLlm(req =>
            {
                prefix = req.StablePrefix;
                Assert.Contains("CANON_LAYER_ONE_FROM_FILE", req.StablePrefix, StringComparison.Ordinal);
                Assert.DoesNotContain("Translate to Russian. Keep HTML.", req.StablePrefix, StringComparison.Ordinal);
                Assert.Contains("## Librarian", req.StablePrefix, StringComparison.Ordinal);
                Assert.Contains("Библиарий", req.StablePrefix, StringComparison.Ordinal);
                Assert.DoesNotContain("UNIQUE_CHAPTER_PHRASE", req.StablePrefix, StringComparison.Ordinal);
                Assert.DoesNotContain("Eisenhorn", req.StablePrefix, StringComparison.Ordinal);
                Assert.Contains("UNIQUE_CHAPTER_PHRASE", req.VariableContent, StringComparison.Ordinal);
                return Ok("<p>Уникальная фраза Эйзенхорн Библиарий</p>");
            });

            var loader = new StyleRulesLoader(promptRoot);
            var result = await CreateService(llm.Object, styleRulesLoader: loader).RunAsync(
                Job(input, glossary, output, workDir),
                CancellationToken.None);

            Assert.False(result.HasFailures);
            Assert.NotNull(prefix);
            var candidates = await File.ReadAllTextAsync(Path.Combine(workDir, "candidates.md"));
            Assert.Contains("Eisenhorn", candidates, StringComparison.Ordinal);
            Assert.DoesNotContain("Eisenhorn", prefix, StringComparison.Ordinal);

            var resumeLlm = new Mock<ILlmProvider>(MockBehavior.Strict);
            var resume = await CreateService(resumeLlm.Object, styleRulesLoader: loader).RunAsync(
                new TranslationJob
                {
                    InputPath = input,
                    GlossaryPath = glossary,
                    OutputPath = output,
                    WorkDir = workDir,
                    Resume = true
                },
                CancellationToken.None);
            Assert.False(resume.HasFailures);
            resumeLlm.Verify(
                x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_InvalidChapterRange_FailsBeforeLlm()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(
                root,
                "<p>One</p>",
                "<p>Two</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var llm = new Mock<ILlmProvider>(MockBehavior.Strict);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateService(llm.Object).RunAsync(
                    Job(input, glossary, output, workDir, chapters: "9"),
                    CancellationToken.None));
            Assert.Contains("2", ex.Message, StringComparison.Ordinal);
            llm.Verify(
                x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_ResumeWithDifferentChapters_Throws()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(
                root,
                "<p>Alpha</p>",
                "<p>Beta</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var llm = CreateMappingLlm(_ => Ok("<p>Альфа</p>"));
            var service = CreateService(llm.Object);
            await service.RunAsync(Job(input, glossary, output, workDir, chapters: "1"), CancellationToken.None);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RunAsync(
                    new TranslationJob
                    {
                        InputPath = input,
                        GlossaryPath = glossary,
                        OutputPath = output,
                        WorkDir = workDir,
                        Resume = true,
                        Chapters = "2"
                    },
                    CancellationToken.None));
            Assert.Contains("chapters", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RunAsync_RetryableLlmError_IsRetried()
    {
        var root = NewRoot();
        try
        {
            var input = MinimalEpubFactory.Create(root, "<p>RetryHttp</p>");
            var glossary = await WriteGlossaryAsync(root);
            var output = Path.Combine(root, "out.epub");
            var workDir = Path.Combine(root, "work");
            var calls = 0;
            var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
            llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        return Task.FromException<LlmResponse>(
                            new LlmException("HTTP 429", isRetryable: true, httpStatusCode: 429));
                    }

                    return Task.FromResult(Ok("<p>После паузы</p>"));
                });

            var result = await CreateService(llm.Object, maxRetries: 3).RunAsync(
                Job(input, glossary, output, workDir),
                CancellationToken.None);

            Assert.False(result.HasFailures);
            Assert.Equal(2, calls);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static BookTranslationService CreateService(
        ILlmProvider llm,
        int maxRetries = 3,
        StyleRulesLoader? styleRulesLoader = null)
    {
        return new BookTranslationService(
            new EpubBookService(),
            new GlossaryParser(),
            new TranslationPromptFactory(new GlossaryWriter()),
            new LengthTokenEstimator(),
            new ChapterChunker(new LengthTokenEstimator()),
            new TranslationValidator(),
            new FileCheckpointStore(),
            llm,
            Options.Create(new TranslatorOptions
            {
                MaxRetries = maxRetries,
                Temperature = 0.1,
                StyleRules = "Translate to Russian. Keep HTML."
            }),
            Options.Create(new LlmOptions
            {
                Model = "test-model",
                ContextWindowTokens = 8000,
                ReservedOutputTokens = 500
            }),
            TimeProvider.System,
            styleRulesLoader: styleRulesLoader);
    }

    private static TranslationJob Job(
        string input,
        string glossary,
        string output,
        string workDir,
        string? chapters = null) =>
        new()
        {
            InputPath = input,
            GlossaryPath = glossary,
            OutputPath = output,
            WorkDir = workDir,
            Chapters = chapters
        };

    private static Mock<ILlmProvider> CreateMappingLlm(Func<LlmRequest, LlmResponse> map)
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmRequest req, CancellationToken _) => map(req));
        return llm;
    }

    private static LlmResponse Ok(string html) => new()
    {
        Content = html,
        FinishReason = "stop",
        PromptTokens = 16
    };

    private static async Task<string> WriteGlossaryAsync(string directory)
    {
        var path = Path.Combine(directory, "working.md");
        await File.WriteAllTextAsync(
            path,
            """
            # Test

            Keep names.

            ## Librarian
            - ru: Библиарий
            - type: title
            """);
        return path;
    }

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
