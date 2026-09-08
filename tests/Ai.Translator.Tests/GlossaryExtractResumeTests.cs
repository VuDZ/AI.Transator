using System.CommandLine;
using System.Text;
using Ai.Translator.Cli;
using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Llm;
using Ai.Translator.Core.Options;
using Ai.Translator.Core.Progress;
using Ai.Translator.Core.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Ai.Translator.Tests;

public sealed class GlossaryExtractResumeTests
{
    [Fact]
    public async Task ExtractAsync_RetryableErrorsThenSuccess_SingleProgressStep()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        llm.SetupSequence(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LlmException("LLM gateway error (HTTP 503): busy", isRetryable: true, httpStatusCode: 503))
            .ThrowsAsync(new LlmException("LLM gateway error (HTTP 503): busy", isRetryable: true, httpStatusCode: 503))
            .ReturnsAsync(EntryResponse("Librarian", "Библиарий"));

        var progress = new RecordingRunProgress();
        var extractor = CreateExtractor(llm.Object, progress, maxRetries: 3, timeProvider: new ImmediateTimeProvider());

        var document = await extractor.ExtractAsync(
            Book(Chapter("OEBPS/A.xhtml", "Alpha unique")),
            Book(Chapter("OEBPS/X.xhtml", "Икс уникальный")),
            existingCorpus: null,
            model: "test-model",
            pairMap: null,
            maxPairs: null,
            CancellationToken.None);

        Assert.Equal("Librarian", Assert.Single(document.Entries).English);
        Assert.Equal(1, progress.TotalSteps);
        Assert.Single(progress.Labels);
        Assert.Single(progress.Ends);
        Assert.Equal(0, progress.Ends[0].Last.FailedCount);
        Assert.True(progress.Completed);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ExtractAsync_ExhaustedRetriesOnSecondPair_KeepsFirstPairOnDisk()
    {
        var root = NewRoot();
        try
        {
            var originalPath = Path.Combine(root, "original.epub");
            var translationPath = Path.Combine(root, "translation.epub");
            var outPath = Path.Combine(root, "extracted.md");
            var workDir = Path.Combine(root, "extract.work");
            await File.WriteAllTextAsync(originalPath, "placeholder");
            await File.WriteAllTextAsync(translationPath, "placeholder");

            var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
            llm.Setup(x => x.CompleteAsync(
                    It.Is<LlmRequest>(req => req.VariableContent.Contains("Alpha", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EntryResponse("Librarian", "Библиарий"));
            llm.Setup(x => x.CompleteAsync(
                    It.Is<LlmRequest>(req => req.VariableContent.Contains("Bravo", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new LlmException("LLM gateway error (HTTP 503): No provider is currently available", isRetryable: true, httpStatusCode: 503));

            var service = CreateService(llm.Object, TwoChapterEpub(originalPath, translationPath), maxRetries: 1);
            var ex = await Assert.ThrowsAsync<LlmException>(() =>
                service.ExtractAsync(
                    originalPath,
                    translationPath,
                    outPath,
                    mergeIntoPath: null,
                    model: "test-model",
                    pairsPath: null,
                    maxPairs: null,
                    CancellationToken.None,
                    workDir,
                    resume: false));

            Assert.Contains("HTTP 503", ex.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(outPath));
            var parsed = new GlossaryParser().Parse(await File.ReadAllTextAsync(outPath));
            Assert.Equal("Librarian", Assert.Single(parsed.Entries).English);
            Assert.DoesNotContain("Guilliman", await File.ReadAllTextAsync(outPath), StringComparison.Ordinal);
            llm.Verify(
                x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExtractAsync_Resume_SkipsCompletedPairs()
    {
        var root = NewRoot();
        try
        {
            var originalPath = Path.Combine(root, "original.epub");
            var translationPath = Path.Combine(root, "translation.epub");
            var outPath = Path.Combine(root, "extracted.md");
            var workDir = Path.Combine(root, "extract.work");
            await File.WriteAllTextAsync(originalPath, "placeholder");
            await File.WriteAllTextAsync(translationPath, "placeholder");
            var epub = TwoChapterEpub(originalPath, translationPath);

            var failing = new Mock<ILlmProvider>(MockBehavior.Strict);
            failing.Setup(x => x.CompleteAsync(
                    It.Is<LlmRequest>(req => req.VariableContent.Contains("Alpha", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EntryResponse("Librarian", "Библиарий"));
            failing.Setup(x => x.CompleteAsync(
                    It.Is<LlmRequest>(req => req.VariableContent.Contains("Bravo", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new LlmException("LLM gateway error (HTTP 503): down", isRetryable: true, httpStatusCode: 503));

            await Assert.ThrowsAsync<LlmException>(() =>
                CreateService(failing.Object, epub, maxRetries: 1).ExtractAsync(
                    originalPath,
                    translationPath,
                    outPath,
                    mergeIntoPath: null,
                    model: "test-model",
                    pairsPath: null,
                    maxPairs: null,
                    CancellationToken.None,
                    workDir,
                    resume: false));

            var resuming = new Mock<ILlmProvider>(MockBehavior.Strict);
            resuming.Setup(x => x.CompleteAsync(
                    It.Is<LlmRequest>(req => req.VariableContent.Contains("Bravo", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(EntryResponse("Guilliman", "Жиллиман"));

            await CreateService(resuming.Object, epub, maxRetries: 1).ExtractAsync(
                originalPath,
                translationPath,
                outPath,
                mergeIntoPath: null,
                model: "test-model",
                pairsPath: null,
                maxPairs: null,
                CancellationToken.None,
                workDir,
                resume: true);

            var parsed = new GlossaryParser().Parse(await File.ReadAllTextAsync(outPath));
            Assert.Equal(2, parsed.Entries.Count);
            Assert.Equal("Librarian", parsed.Entries[0].English);
            Assert.Equal("Guilliman", parsed.Entries[1].English);
            resuming.Verify(
                x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            resuming.Verify(
                x => x.CompleteAsync(
                    It.Is<LlmRequest>(req => req.VariableContent.Contains("Alpha", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExtractAsync_ResumeWithDifferentMaxPairs_ThrowsBeforeLlm()
    {
        var root = NewRoot();
        try
        {
            var originalPath = Path.Combine(root, "original.epub");
            var translationPath = Path.Combine(root, "translation.epub");
            var outPath = Path.Combine(root, "extracted.md");
            var workDir = Path.Combine(root, "extract.work");
            var pairsPath = Path.Combine(root, "pairs.txt");
            await File.WriteAllTextAsync(originalPath, "placeholder");
            await File.WriteAllTextAsync(translationPath, "placeholder");
            await File.WriteAllTextAsync(
                pairsPath,
                "OEBPS/A.xhtml = OEBPS/X.xhtml\nOEBPS/B.xhtml = OEBPS/Y.xhtml\n");

            var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
            llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(EntryResponse("Librarian", "Библиарий"));

            await CreateService(llm.Object, TwoChapterEpub(originalPath, translationPath)).ExtractAsync(
                originalPath,
                translationPath,
                outPath,
                mergeIntoPath: null,
                model: "test-model",
                pairsPath,
                maxPairs: 1,
                CancellationToken.None,
                workDir,
                resume: false);

            var blocked = new Mock<ILlmProvider>(MockBehavior.Strict);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateService(blocked.Object, TwoChapterEpub(originalPath, translationPath)).ExtractAsync(
                    originalPath,
                    translationPath,
                    outPath,
                    mergeIntoPath: null,
                    model: "test-model",
                    pairsPath,
                    maxPairs: 2,
                    CancellationToken.None,
                    workDir,
                    resume: true));

            Assert.Equal(GlossaryExtractor.ResumeMismatchMessage, ex.Message);
            blocked.Verify(
                x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ExtractAsync_ResumeWithoutState_Throws()
    {
        var root = NewRoot();
        try
        {
            var originalPath = Path.Combine(root, "original.epub");
            var translationPath = Path.Combine(root, "translation.epub");
            var outPath = Path.Combine(root, "extracted.md");
            var workDir = Path.Combine(root, "extract.work");
            Directory.CreateDirectory(workDir);
            await File.WriteAllTextAsync(originalPath, "placeholder");
            await File.WriteAllTextAsync(translationPath, "placeholder");

            var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateService(llm.Object, TwoChapterEpub(originalPath, translationPath)).ExtractAsync(
                    originalPath,
                    translationPath,
                    outPath,
                    mergeIntoPath: null,
                    model: "test-model",
                    pairsPath: null,
                    maxPairs: null,
                    CancellationToken.None,
                    workDir,
                    resume: true));

            Assert.Equal(GlossaryExtractor.ResumeStateMissingMessage, ex.Message);
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
    public async Task ListPairs_WithResume_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--list-pairs",
            "--resume");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.ListPairsConflictMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListPairs_WithWorkDir_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--list-pairs",
            "--work-dir", "work");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.ListPairsConflictMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void HasListPairsConflict_WhenWorkDirOrResumePresent()
    {
        Assert.True(GlossaryExtractArguments.HasListPairsConflict(true, null, null, null, "work", resume: false));
        Assert.True(GlossaryExtractArguments.HasListPairsConflict(true, null, null, null, null, resume: true));
        Assert.False(GlossaryExtractArguments.HasListPairsConflict(true, null, null, null, null, resume: false));
    }

    [Fact]
    public void ResolveWorkDir_DefaultsNextToOutStem()
    {
        var resolved = GlossaryExtractService.ResolveWorkDir(@"C:\books\universe-corpus.draft.md", null);
        Assert.Equal(@"C:\books\universe-corpus.draft.extract.work", resolved);
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

    private static GlossaryExtractService CreateService(
        ILlmProvider llm,
        IEpubBookService epub,
        int maxRetries = 3)
    {
        return new GlossaryExtractService(
            epub,
            new GlossaryParser(),
            new GlossaryWriter(),
            CreateExtractor(llm, maxRetries: maxRetries, timeProvider: new ImmediateTimeProvider()),
            new GlossaryPairMapParser());
    }

    private static GlossaryExtractor CreateExtractor(
        ILlmProvider llm,
        IRunProgress? progress = null,
        int maxRetries = 3,
        TimeProvider? timeProvider = null)
    {
        return new GlossaryExtractor(
            llm,
            new GlossaryParser(),
            new GlossaryWriter(),
            new GlossaryMerger(),
            new LengthTokenEstimator(),
            new ExtractRulesLoader(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            Options.Create(new TranslatorOptions { Temperature = 0.1, MaxRetries = maxRetries }),
            Options.Create(new LlmOptions
            {
                Model = "test-model",
                ContextWindowTokens = 8000,
                ReservedOutputTokens = 500
            }),
            progress: progress,
            timeProvider: timeProvider ?? new ImmediateTimeProvider());
    }

    private static IEpubBookService TwoChapterEpub(string originalPath, string translationPath)
    {
        var original = new Mock<IEpubBookService>(MockBehavior.Strict);
        original.Setup(x => x.OpenAsync(originalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Book(
                Chapter("OEBPS/A.xhtml", "Alpha unique"),
                Chapter("OEBPS/B.xhtml", "Bravo unique")));
        var translation = new Mock<IEpubBookService>(MockBehavior.Strict);
        translation.Setup(x => x.OpenAsync(translationPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Book(
                Chapter("OEBPS/X.xhtml", "Икс уникальный"),
                Chapter("OEBPS/Y.xhtml", "Игрек уникальный")));

        var epub = new Mock<IEpubBookService>(MockBehavior.Strict);
        epub.Setup(x => x.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken ct) =>
                path.Equals(originalPath, StringComparison.OrdinalIgnoreCase)
                    ? original.Object.OpenAsync(path, ct)
                    : translation.Object.OpenAsync(path, ct));
        return epub.Object;
    }

    private static LlmResponse EntryResponse(string english, string ru) =>
        new()
        {
            Content =
                $"""
                ## {english}
                - ru: {ru}
                - type: title
                """,
            FinishReason = "stop"
        };

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

    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                callback(state);
            }

            return new CompletedTimer();
        }

        private sealed class CompletedTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
