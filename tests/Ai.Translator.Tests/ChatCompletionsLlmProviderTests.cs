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

public sealed class ChatCompletionsLlmProviderTests
{
    [Fact]
    public async Task CompleteAsync_OpenRouter_PutsCacheControlOnSystemPrefix()
    {
        string? body = null;
        Uri? requestUri = null;
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            requestUri = request.RequestUri;
            body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return CompletionResponse("<p>Hi</p>", cachedTokens: 42);
        });

        var provider = CreateProvider(handler, OpenRouterOptions());
        var response = await provider.CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal("<p>Hi</p>", response.Content);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal(12, response.PromptTokens);
        Assert.Equal(42, response.CachedTokens);
        Assert.NotNull(requestUri);
        Assert.EndsWith("/chat/completions", requestUri.AbsolutePath, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(500, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("temperature", out _));

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("<p>Chapter text</p>", messages[1].GetProperty("content").GetString());

        var systemContent = messages[0].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, systemContent.ValueKind);
        Assert.Equal("stable prefix", systemContent[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", systemContent[0].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Contains("cache_control", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_None_OmitsCacheControlAndPutsPrefixInSystem()
    {
        string? body = null;
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return CompletionResponse("<p>Ok</p>");
        });

        var provider = CreateProvider(handler, NoneOptions());
        var response = await provider.CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.Null(response.CachedTokens);
        Assert.DoesNotContain("cache_control", body, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(500, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("temperature", out _));
        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("stable prefix", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("<p>Chapter text</p>", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_SendTemperatureTrue_IncludesRequestTemperature()
    {
        string? body = null;
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return CompletionResponse("<p>Ok</p>");
        });

        var options = NoneOptions();
        options.SendTemperature = true;
        var provider = CreateProvider(handler, options);
        await provider.CompleteAsync(SampleRequest(), CancellationToken.None);

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(500, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task CompleteAsync_ParsesFlatCachedTokensWhenDetailsMissing()
    {
        const string payload =
            "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":9,\"cached_tokens\":7}}";
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(payload)));

        var provider = CreateProvider(handler, NoneOptions());
        var response = await provider.CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(9, response.PromptTokens);
        Assert.Equal(7, response.CachedTokens);
    }

    [Fact]
    public async Task CompleteAsync_ParsesCompletionTokensFromUsage()
    {
        const string payload =
            "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":4,\"cached_tokens\":2}}";
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(payload)));

        var provider = CreateProvider(handler, NoneOptions());
        var response = await provider.CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(11, response.PromptTokens);
        Assert.Equal(4, response.CompletionTokens);
        Assert.Equal(2, response.CachedTokens);
    }

    [Fact]
    public async Task CompleteAsync_MissingCompletionTokens_LeavesNull()
    {
        const string payload =
            "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":9}}";
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(payload)));

        var provider = CreateProvider(handler, NoneOptions());
        var response = await provider.CompleteAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(9, response.PromptTokens);
        Assert.Null(response.CompletionTokens);
    }

    [Fact]
    public async Task CompleteAsync_EmptyApiKey_ThrowsBeforeHttp()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var options = NoneOptions();
        options.ApiKey = string.Empty;
        var provider = new ChatCompletionsLlmProvider(factory.Object, Options.Create(options));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CompleteAsync(SampleRequest(), CancellationToken.None));

        Assert.Equal(ChatCompletionsLlmProvider.MissingLocalConfigMessage, ex.Message);
        Assert.Contains("appsettings.Local.json.example", ex.Message, StringComparison.Ordinal);
        factory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_EmptyBaseUrl_ThrowsBeforeHttp()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var options = NoneOptions();
        options.BaseUrl = " ";
        var provider = new ChatCompletionsLlmProvider(factory.Object, Options.Create(options));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CompleteAsync(SampleRequest(), CancellationToken.None));

        Assert.Contains("appsettings.Local.json.example", ex.Message, StringComparison.Ordinal);
        factory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CompleteAsync_NonPositiveTimeout_ThrowsBeforeHttp(int timeoutSeconds)
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var options = NoneOptions();
        options.TimeoutSeconds = timeoutSeconds;
        var provider = new ChatCompletionsLlmProvider(factory.Object, Options.Create(options));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CompleteAsync(SampleRequest(), CancellationToken.None));

        Assert.Contains("TimeoutSeconds", ex.Message, StringComparison.Ordinal);
        factory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    public async Task CompleteAsync_HttpStatus_MapsRetryableFlag(HttpStatusCode status, bool retryable)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("error", Encoding.UTF8, "application/json")
        }));

        var provider = CreateProvider(handler, NoneOptions());
        var ex = await Assert.ThrowsAsync<LlmException>(
            () => provider.CompleteAsync(SampleRequest(), CancellationToken.None));

        Assert.Equal(retryable, ex.IsRetryable);
        Assert.Equal((int)status, ex.HttpStatusCode);
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Assert.Contains("key or gateway", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LlmOptions_TimeoutSeconds_DefaultsTo300()
    {
        Assert.Equal(300, new LlmOptions().TimeoutSeconds);
    }

    [Fact]
    public void LlmOptions_SendTemperature_DefaultsToFalse()
    {
        Assert.False(new LlmOptions().SendTemperature);
    }

    private static ChatCompletionsLlmProvider CreateProvider(HttpMessageHandler handler, LlmOptions options)
    {
        var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(Math.Max(options.TimeoutSeconds, 1))
        };

        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory.Setup(x => x.CreateClient(ServiceCollectionExtensions.LlmHttpClientName)).Returns(client);
        return new ChatCompletionsLlmProvider(factory.Object, Options.Create(options));
    }

    private static LlmOptions NoneOptions() => new()
    {
        BaseUrl = "https://gateway.test/v1",
        Model = "test-model",
        ApiKey = "test-key",
        TimeoutSeconds = 300,
        CacheMode = "none"
    };

    private static LlmOptions OpenRouterOptions() => new()
    {
        BaseUrl = "https://openrouter.ai/api/v1",
        Model = "test-model",
        ApiKey = "test-key",
        TimeoutSeconds = 300,
        CacheMode = "openrouter"
    };

    private static LlmRequest SampleRequest() => new()
    {
        Model = "test-model",
        StablePrefix = "stable prefix",
        VariableContent = "<p>Chapter text</p>",
        MaxOutputTokens = 500,
        Temperature = 0.2
    };

    private static HttpResponseMessage CompletionResponse(string content, int? cachedTokens = null)
    {
        var usage = cachedTokens is null
            ? "{\"prompt_tokens\":12}"
            : "{\"prompt_tokens\":12,\"prompt_tokens_details\":{\"cached_tokens\":" + cachedTokens.Value + "}}";
        var json = "{\"choices\":[{\"message\":{\"content\":\"" + content + "\"},\"finish_reason\":\"stop\"}],\"usage\":" + usage + "}";
        return JsonResponse(json);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _send(request, cancellationToken);
    }
}
