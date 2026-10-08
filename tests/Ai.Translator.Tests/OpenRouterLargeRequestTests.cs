using System.Net;
using System.Text;
using System.Text.Json;
using Ai.Translator.Core;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Llm;
using Ai.Translator.Core.Options;
using Microsoft.Extensions.Options;
using Moq;

namespace Ai.Translator.Tests;

public sealed class OpenRouterLargeRequestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteAsync_CollectsOnlyTranslationAndUsage(bool emptyUsageChoices)
    {
        var usageChoices = emptyUsageChoices ? "[]" : "[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]";
        var sse = ": keepalive\r\n\r\n"
            + "data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning\":\"hidden\",\"reasoning_details\":[{\"text\":\"hidden\"}]}}]}\n\n"
            + "event: message\ndata: {\"choices\":[\ndata: {\"index\":0,\"delta\":{\"content\":\"<p>Привет \"}}]}\n\n"
            + "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"мир</p>\"},\"finish_reason\":\"stop\"}]}\n\n"
            + "data: {\"choices\":" + usageChoices + ",\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":50,\"prompt_tokens_details\":{\"cached_tokens\":90},\"completion_tokens_details\":{\"reasoning_tokens\":10}}}\n\n"
            + "data: [DONE]";
        var provider = CreateProvider(new ChunkedStream(Encoding.UTF8.GetBytes(sse)), NewOptions());
        var response = await provider.CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("<p>Привет мир</p>", response.Content);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal(100, response.PromptTokens);
        Assert.Equal(90, response.CachedTokens);
        Assert.Equal(50, response.CompletionTokens);
        Assert.Equal(10, response.ReasoningTokens);
    }

    [Theory]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"stop\"}]}\n\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\ndata: [DONE]\n\n")]
    [InlineData("data: not-json\n\ndata: [DONE]\n\n")]
    [InlineData("data: []\n\ndata: [DONE]\n\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"error\"}]}\n\n")]
    public async Task CompleteAsync_IncompleteOrMalformedStream_IsRetryable(string sse)
    {
        var provider = CreateProvider(new MemoryStream(Encoding.UTF8.GetBytes(sse)), NewOptions());
        var error = await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(Request(), CancellationToken.None));
        Assert.True(error.IsRetryable);
    }

    [Theory]
    [InlineData("429", true)]
    [InlineData("502", true)]
    [InlineData("400", false)]
    [InlineData("401", false)]
    [InlineData("402", false)]
    [InlineData("403", false)]
    [InlineData("\"server_error\"", true)]
    public async Task CompleteAsync_MidStreamError_PreservesRetryPolicy(string code, bool retryable)
    {
        var sse = "data: {\"error\":{\"code\":" + code + ",\"message\":\"upstream failed\"},\"choices\":[]}\n\n";
        var provider = CreateProvider(new MemoryStream(Encoding.UTF8.GetBytes(sse)), NewOptions());
        var error = await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal(retryable, error.IsRetryable);
        Assert.Contains("upstream failed", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteAsync_TimeoutAfterHeaders_IsRetryable(bool streaming)
    {
        var options = NewOptions();
        options.Stream = streaming;
        options.TimeoutSeconds = 1;
        var provider = CreateProvider(new WaitingStream(), options);
        var error = await Assert.ThrowsAsync<LlmException>(() => provider.CompleteAsync(Request(), CancellationToken.None));
        Assert.True(error.IsRetryable);
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task CompleteAsync_ExternalCancellationAfterHeaders_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        var waiting = new WaitingStream();
        var provider = CreateProvider(waiting, NewOptions());
        var run = provider.CompleteAsync(Request(), cancellation.Token);
        await waiting.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("5m")]
    [InlineData("1h")]
    public async Task CompleteAsync_CacheTtlAndSession_AreStableAcrossChunks(string? ttl)
    {
        var options = NewOptions();
        options.CacheTtl = ttl;
        options.ReasoningEffort = "low";
        var bodies = new List<string>();
        var handler = new Handler(async (request, token) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(token));
            return SseResponse(new MemoryStream(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")));
        });
        var provider = CreateProvider(handler, options);
        await provider.CompleteAsync(Request("first"), CancellationToken.None);
        await provider.CompleteAsync(Request("second"), CancellationToken.None);
        using var first = JsonDocument.Parse(bodies[0]);
        using var second = JsonDocument.Parse(bodies[1]);
        var root = first.RootElement;
        Assert.Equal(root.GetProperty("session_id").GetString(), second.RootElement.GetProperty("session_id").GetString());
        Assert.Equal(64, root.GetProperty("session_id").GetString()!.Length);
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.True(root.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.Equal(128000, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(root.TryGetProperty("max_tokens", out _));
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.True(root.GetProperty("reasoning").GetProperty("exclude").GetBoolean());
        var cache = root.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("cache_control");
        if (ttl is null)
        {
            Assert.False(cache.TryGetProperty("ttl", out _));
        }
        else
        {
            Assert.Equal(ttl, cache.GetProperty("ttl").GetString());
        }
        Assert.False(root.GetProperty("messages")[1].TryGetProperty("cache_control", out _));
    }

    [Fact]
    public async Task CompleteAsync_NoneReasoning_DisablesThinkingWithoutCacheFields()
    {
        string? body = null;
        var options = NewOptions();
        options.CacheMode = "none";
        options.CacheTtl = "1h";
        options.Stream = false;
        options.ReasoningEffort = "none";
        var provider = CreateProvider(new Handler(async (request, token) =>
        {
            body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"thinking\",\"text\":\"hidden\"},{\"type\":\"text\",\"text\":\"перевод\"}]},\"finish_reason\":\"stop\"}]}")
            };
        }), options);
        Assert.Equal("перевод", (await provider.CompleteAsync(Request(), CancellationToken.None)).Content);
        using var json = JsonDocument.Parse(body!);
        Assert.False(json.RootElement.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("session_id", out _));
        Assert.False(json.RootElement.TryGetProperty("stream_options", out _));
        Assert.DoesNotContain("cache_control", body!);
        Assert.Equal(128000, json.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Theory]
    [InlineData("invalid", null)]
    [InlineData(null, "10m")]
    public async Task CompleteAsync_InvalidOptions_FailsBeforeHttp(string? effort, string? ttl)
    {
        var options = NewOptions();
        options.ReasoningEffort = effort;
        options.CacheTtl = ttl;
        var handler = new Handler((_, _) => throw new InvalidOperationException("HTTP must not run"));
        var provider = CreateProvider(handler, options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(Request(), CancellationToken.None));
        Assert.Contains(effort is null ? "CacheTtl" : "ReasoningEffort", error.Message);
    }

    private static LlmOptions NewOptions() => new()
    {
        BaseUrl = "https://example.test/api/v1/",
        ApiKey = "test-key",
        CacheMode = "openrouter",
        Stream = true
    };

    private static LlmRequest Request(string content = "source") => new()
    {
        Model = "anthropic/claude-haiku-5.5",
        StablePrefix = "stable prefix",
        VariableContent = content,
        MaxOutputTokens = 128000
    };

    private static ChatCompletionsLlmProvider CreateProvider(Stream stream, LlmOptions options) =>
        CreateProvider(new Handler((_, _) => Task.FromResult(SseResponse(stream))), options);

    private static ChatCompletionsLlmProvider CreateProvider(HttpMessageHandler handler, LlmOptions options)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(ServiceCollectionExtensions.LlmHttpClientName))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) });
        return new ChatCompletionsLlmProvider(factory.Object, Options.Create(options));
    }

    private static HttpResponseMessage SseResponse(Stream stream) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(stream)
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class ChunkedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
    }

    private sealed class WaitingStream : MemoryStream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
