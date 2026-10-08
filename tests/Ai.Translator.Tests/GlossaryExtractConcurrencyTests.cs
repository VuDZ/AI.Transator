using System.Collections.Concurrent;
using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ai.Translator.Cli;
using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Llm;
using Ai.Translator.Core.Options;
using Ai.Translator.Core.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Ai.Translator.Tests;

public sealed class GlossaryExtractConcurrencyTests
{
    [Fact]
    public async Task ExtractAsync_WarmsCacheThenBoundsParallelismAndMergesInPlanOrder()
    {
        await using var fixture = new Fixture();
        var llm = new ControlledProvider();
        var progress = new SignalingProgress();
        var run = fixture.Start(llm, 4, concurrency: 2, progress: progress);
        await llm.WaitForStartAsync(1);
        Assert.False(llm.HasStarted(2));
        llm.Complete(1, "First", "Первый");
        await Task.WhenAll(llm.WaitForStartAsync(2), llm.WaitForStartAsync(3));
        Assert.False(llm.HasStarted(4));
        llm.Complete(3, "Shared", "Поздний");
        await fixture.Store.ThirdPending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var interim = await fixture.ReadDocumentAsync();
        Assert.Equal("First", Assert.Single(interim.Entries).English);
        await llm.WaitForStartAsync(4);
        llm.Complete(4, "Last", "Последний");
        llm.Complete(2, "Shared", "Ранний");
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "First", "Shared", "Last" }, result.Entries.Select(e => e.English));
        Assert.Equal("Ранний", result.Entries[1].Ru);
        Assert.Equal(2, llm.Peak);
        Assert.Equal(1, fixture.Store.PeakWriters);
        Assert.Single(llm.Requests.Select(r => r.StablePrefix).Distinct());
        var state = await fixture.ReadStateAsync();
        Assert.Equal(4, state.CompletedSteps.Count);
        Assert.Empty(state.PendingOutputs);
        Assert.Equal(new[] { "First", "Shared", "Last" }, (await fixture.ReadDocumentAsync()).Entries.Select(e => e.English));
        Assert.Equal(4, progress.Recording.TotalSteps);
        Assert.Equal(4, progress.Recording.Ends.Count);
        Assert.Equal(10, progress.Recording.CompletedTotals!.PromptTokens);
        Assert.Equal(0, progress.Recording.CompletedTotals.FailedCount);
    }

    [Fact]
    public async Task ExtractAsync_DefaultConcurrency_IsSequential()
    {
        await using var fixture = new Fixture();
        var llm = new ControlledProvider();
        var run = fixture.Start(llm, 3);
        await llm.WaitForStartAsync(1);
        Assert.False(llm.HasStarted(2));
        llm.Complete(1, "First", "Первый");
        await llm.WaitForStartAsync(2);
        Assert.False(llm.HasStarted(3));
        llm.Complete(2, "Second", "Второй");
        await llm.WaitForStartAsync(3);
        llm.Complete(3, "Third", "Третий");
        Assert.Equal(3, (await run.WaitAsync(TimeSpan.FromSeconds(5))).Entries.Count);
        Assert.Equal(1, llm.Peak);
        Assert.Equal(1, new TranslatorOptions().ExtractMaxConcurrency);
    }

    [Fact]
    public async Task ExtractAsync_FailureDrainsActiveRequestsAndResumeUsesPaidPendingOutput()
    {
        await using var fixture = new Fixture();
        var llm = new ControlledProvider();
        var progress = new SignalingProgress();
        var run = fixture.Start(llm, 4, concurrency: 2, progress: progress);
        await llm.WaitForStartAsync(1);
        llm.Complete(1, "First", "Первый");
        await Task.WhenAll(llm.WaitForStartAsync(2), llm.WaitForStartAsync(3));
        llm.Fail(2, new LlmException("Insufficient credits", false, 402));
        await progress.Failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(run.IsCompleted);
        llm.Complete(3, "Shared", "Поздний");
        var error = await Assert.ThrowsAsync<LlmException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(402, error.HttpStatusCode);
        Assert.False(llm.HasStarted(4));
        Assert.Equal(0, llm.Active);
        var state = await fixture.ReadStateAsync();
        Assert.Single(state.CompletedSteps);
        Assert.Single(state.PendingOutputs);
        Assert.Contains("Поздний", Assert.Single(state.PendingOutputs).Value);

        var resumed = new Mock<ILlmProvider>(MockBehavior.Strict);
        var calls = new ConcurrentQueue<int>();
        resumed.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmRequest request, CancellationToken _) =>
            {
                var index = Index(request);
                calls.Enqueue(index);
                return index switch
                {
                    2 => Entry("Shared", "Ранний", 2),
                    4 => Entry("Last", "Последний", 4),
                    _ => throw new InvalidOperationException("Completed and pending responses must not be regenerated.")
                };
            });
        var result = await fixture.Start(resumed.Object, 4, concurrency: 1, resume: true);
        Assert.Equal(new[] { 2, 4 }, calls);
        Assert.Equal(new[] { "First", "Shared", "Last" }, result.Entries.Select(e => e.English));
        Assert.Equal("Ранний", result.Entries[1].Ru);
        Assert.Empty((await fixture.ReadStateAsync()).PendingOutputs);
        Assert.Equal(4, (await fixture.ReadStateAsync()).CompletedSteps.Count);
    }

    [Fact]
    public async Task ExtractAsync_CancellationDrainsTasksAndKeepsCompletedResponse()
    {
        await using var fixture = new Fixture();
        var llm = new ControlledProvider();
        var run = fixture.Start(llm, 3, concurrency: 2);
        await llm.WaitForStartAsync(1);
        llm.Complete(1, "First", "Первый");
        await Task.WhenAll(llm.WaitForStartAsync(2), llm.WaitForStartAsync(3));
        llm.Complete(3, "Third", "Третий");
        await fixture.Store.ThirdPending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, llm.Active);
        var state = await fixture.ReadStateAsync();
        Assert.Single(state.CompletedSteps);
        Assert.Single(state.PendingOutputs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task ExtractAsync_InvalidConfiguredConcurrency_RejectsBeforeLlm(int width)
    {
        await using var fixture = new Fixture();
        var blocked = new Mock<ILlmProvider>(MockBehavior.Strict);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Start(blocked.Object, 2, configuredConcurrency: width));
        Assert.Contains("between 1 and 8", error.Message);
        blocked.Verify(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ResumeAcceptsLegacyCheckpointWithoutPendingOutputs()
    {
        await using var fixture = new Fixture();
        var first = new Mock<ILlmProvider>(MockBehavior.Strict);
        first.SetupSequence(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entry("First", "Первый", 1))
            .ThrowsAsync(new LlmException("Busy", true, 503));
        await Assert.ThrowsAsync<LlmException>(() => fixture.Start(first.Object, 2));
        var path = Path.Combine(fixture.WorkDir, "state.json");
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        legacy.Remove("PendingOutputs");
        await File.WriteAllTextAsync(path, legacy.ToJsonString());

        var resumed = new Mock<ILlmProvider>(MockBehavior.Strict);
        resumed.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entry("Second", "Второй", 2));
        Assert.Equal(2, (await fixture.Start(resumed.Object, 2, concurrency: 2, resume: true)).Entries.Count);
        resumed.Verify(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task Cli_InvalidConcurrency_IsRejectedBeforeService(int width)
    {
        var service = new CaptureService();
        var (exit, error) = await InvokeCliAsync(service, "--out", "out.md", "--concurrency", width.ToString());
        Assert.NotEqual(0, exit);
        Assert.Contains("between 1 and 8", error);
        Assert.False(service.Called);
    }

    [Fact]
    public async Task Cli_ListPairsWithConcurrency_IsRejectedBeforeService()
    {
        var service = new CaptureService();
        var (exit, error) = await InvokeCliAsync(service, "--list-pairs", "--concurrency", "2");
        Assert.NotEqual(0, exit);
        Assert.Contains("--list-pairs cannot be combined", error);
        Assert.False(service.Called);
    }

    [Fact]
    public async Task Cli_Concurrency_IsPassedToExtractService()
    {
        var service = new CaptureService();
        var (exit, error) = await InvokeCliAsync(service, "--out", "out.md", "--concurrency", "3");
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        Assert.True(service.Called);
        Assert.Equal(3, service.Concurrency);
    }

    private static async Task<(int Exit, string Error)> InvokeCliAsync(CaptureService service, params string[] options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGlossaryExtractService>(service);
        await using var provider = services.BuildServiceProvider();
        var args = new[] { "glossary", "extract", "--original", "original.epub", "--translation", "translation.epub" }.Concat(options).ToArray();
        var parse = CommandTree.Create(provider).Parse(args);
        var error = new StringWriter();
        parse.InvocationConfiguration.Error = error;
        parse.InvocationConfiguration.Output = new StringWriter();
        return (await parse.InvokeAsync(), error.ToString());
    }

    private static int Index(LlmRequest request) =>
        int.Parse(Regex.Match(request.VariableContent, @"Original \[(\d+)/").Groups[1].Value);

    private static LlmResponse Entry(string english, string ru, int index) => new()
    {
        Content = $"## {english}\n- ru: {ru}\n- type: title\n",
        FinishReason = "stop",
        PromptTokens = index,
        CachedTokens = 1,
        CompletionTokens = 10
    };

    private sealed class ControlledProvider : ILlmProvider
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<LlmResponse>> _responses = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _started = new();
        private int _active;
        private int _peak;
        public int Active => Volatile.Read(ref _active);
        public int Peak => Volatile.Read(ref _peak);
        public ConcurrentQueue<LlmRequest> Requests { get; } = new();

        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            var index = Index(request);
            Requests.Enqueue(request);
            var active = Interlocked.Increment(ref _active);
            int observed;
            do
            {
                observed = Volatile.Read(ref _peak);
                if (active <= observed)
                {
                    break;
                }
            } while (Interlocked.CompareExchange(ref _peak, active, observed) != observed);
            _started.GetOrAdd(index, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            try
            {
                return await Response(index).Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private TaskCompletionSource<LlmResponse> Response(int index) =>
            _responses.GetOrAdd(index, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

        public Task WaitForStartAsync(int index) => _started.GetOrAdd(index, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TimeSpan.FromSeconds(5));
        public bool HasStarted(int index) => _started.TryGetValue(index, out var signal) && signal.Task.IsCompleted;
        public void Complete(int index, string english, string ru) => Response(index).TrySetResult(Entry(english, ru, index));
        public void Fail(int index, Exception error) => Response(index).TrySetException(error);
    }

    private sealed class SignalingProgress : IRunProgress
    {
        public RecordingRunProgress Recording { get; } = new();
        public TaskCompletionSource Failure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Begin(int steps) => Recording.Begin(steps);
        public void StepBegin(string label) => Recording.StepBegin(label);
        public void StepEnd(LlmUsageSnapshot last, LlmUsageSnapshot totals, TimeSpan eta)
        {
            Recording.StepEnd(last, totals, eta);
            if (last.FailedCount > 0)
            {
                Failure.TrySetResult();
            }
        }
        public void Complete(LlmUsageSnapshot totals) => Recording.Complete(totals);
    }

    private sealed class TrackingStore : IExtractCheckpointStore
    {
        private readonly FileExtractCheckpointStore _inner = new();
        private int _writers;
        private int _peak;
        public int PeakWriters => _peak;
        public TaskCompletionSource ThirdPending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ExtractCheckpointState?> LoadAsync(string workDir, CancellationToken cancellationToken) => _inner.LoadAsync(workDir, cancellationToken);
        public async Task SaveAsync(string workDir, ExtractCheckpointState state, CancellationToken cancellationToken)
        {
            var writers = Interlocked.Increment(ref _writers);
            _peak = Math.Max(_peak, writers);
            try
            {
                await Task.Yield();
                await _inner.SaveAsync(workDir, state, cancellationToken);
                if (state.PendingOutputs.ContainsKey("3:OEBPS/3.xhtml"))
                {
                    ThirdPending.TrySetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _writers);
            }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _stop = new();
        private Task<GlossaryDocument>? _current;
        public Fixture() => Directory.CreateDirectory(_root);
        public string Output => Path.Combine(_root, "out.md");
        public string WorkDir => Path.Combine(_root, "extract.work");
        public TrackingStore Store { get; } = new();

        public Task<GlossaryDocument> Start(ILlmProvider provider, int count, int? concurrency = null,
            bool resume = false, int configuredConcurrency = 1, IRunProgress? progress = null)
        {
            var extractor = new GlossaryExtractor(
                provider, new GlossaryParser(), new GlossaryWriter(), new GlossaryMerger(), new LengthTokenEstimator(),
                new ExtractRulesLoader(Path.Combine(_root, "no-prompts")),
                Options.Create(new TranslatorOptions { ExtractMaxConcurrency = configuredConcurrency, MaxRetries = 1 }),
                Options.Create(new LlmOptions { Model = "test-model", ContextWindowTokens = 8000, ReservedOutputTokens = 500 }),
                progress: progress, checkpoints: Store);
            EpubBookModel Book(bool translated) => new()
            {
                Chapters = Enumerable.Range(1, count).Select(i => new EpubChapter
                {
                    FilePath = $"OEBPS/{i}.xhtml",
                    Xhtml = "<p>Text</p>",
                    BodyInnerHtml = "<p>Text</p>",
                    PlainText = translated ? $"Русский текст {i}" : $"Original text {i}"
                }).ToArray()
            };
            var run = new ExtractRunContext
            {
                OutputPath = Output, WorkDir = WorkDir, OriginalPath = "original.epub",
                TranslationPath = "translation.epub", Concurrency = concurrency, Resume = resume
            };
            _current = extractor.ExtractAsync(Book(false), Book(true), null, "test-model", null, null, _stop.Token, run);
            return _current;
        }

        public void Cancel() => _stop.Cancel();
        public async Task<ExtractCheckpointState> ReadStateAsync() => (await Store.LoadAsync(WorkDir, CancellationToken.None))!;
        public async Task<GlossaryDocument> ReadDocumentAsync() => new GlossaryParser().Parse(await File.ReadAllTextAsync(Output));
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            if (_current is not null)
            {
                try { await _current; } catch { }
            }
            _stop.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class CaptureService : IGlossaryExtractService
    {
        public bool Called { get; private set; }
        public int? Concurrency { get; private set; }
        public Task ExtractAsync(string originalPath, string translationPath, string outputPath, string? mergeIntoPath,
            string? model, string? pairsPath, int? maxPairs, CancellationToken cancellationToken,
            string? workDir = null, bool resume = false, int? concurrency = null)
        {
            Called = true;
            Concurrency = concurrency;
            return Task.CompletedTask;
        }
    }
}
