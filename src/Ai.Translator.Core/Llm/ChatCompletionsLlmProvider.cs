using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ai.Translator.Core.Llm;

public sealed class ChatCompletionsLlmProvider : ILlmProvider
{
    public const string MissingLocalConfigMessage =
        "LLM is not configured. Copy appsettings.Local.json.example to appsettings.Local.json and fill in Llm:BaseUrl, Llm:Model, and Llm:ApiKey.";

    public const int GatewayErrorBodyLimit = 500;

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<LlmOptions> _options;
    private readonly ILogger<ChatCompletionsLlmProvider> _logger;

    public ChatCompletionsLlmProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmOptions> options,
        ILogger<ChatCompletionsLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger ?? NullLogger<ChatCompletionsLlmProvider>.Instance;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var llm = _options.Value;
        ArgumentNullException.ThrowIfNull(llm);
        EnsureConfigured(llm);

        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.LlmHttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(
                BuildRequestJson(request, llm.CacheMode, llm.SendTemperature),
                Encoding.UTF8,
                "application/json")
        };

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client
                .SendAsync(httpRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new LlmException("The LLM request timed out.", isRetryable: true, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException("The LLM gateway is unreachable.", isRetryable: true, innerException: ex);
        }

        using (httpResponse)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = (int)httpResponse.StatusCode;
            if (status == (int)HttpStatusCode.Unauthorized || status == (int)HttpStatusCode.Forbidden)
            {
                throw new LlmException(
                    FormatGatewayError(status, body, credentialsRejected: true),
                    isRetryable: false,
                    httpStatusCode: status);
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                var retryable = status == 429 || status >= 500;
                throw new LlmException(
                    FormatGatewayError(status, body, credentialsRejected: false),
                    isRetryable: retryable,
                    httpStatusCode: status);
            }

            var response = ParseResponse(body);
            _logger.LogInformation(
                "Chat Completions finished: model {Model}, finish {FinishReason}, prompt tokens {PromptTokens}, cached {CachedTokens}.",
                request.Model,
                response.FinishReason,
                response.PromptTokens,
                response.CachedTokens);
            return response;
        }
    }

    private static void EnsureConfigured(LlmOptions llm)
    {
        if (string.IsNullOrWhiteSpace(llm.BaseUrl) || string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            throw new InvalidOperationException(MissingLocalConfigMessage);
        }

        if (llm.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Llm:TimeoutSeconds must be greater than 0 (set it in appsettings.Local.json). Default is 300.");
        }

        if (!IsNoneCacheMode(llm.CacheMode) && !IsOpenRouterCacheMode(llm.CacheMode))
        {
            throw new InvalidOperationException(
                "Llm:CacheMode must be 'none' or 'openrouter' (set it in appsettings.Local.json).");
        }
    }

    private static string BuildRequestJson(LlmRequest request, string? cacheMode, bool sendTemperature)
    {
        JsonNode systemContent;
        if (IsOpenRouterCacheMode(cacheMode))
        {
            systemContent = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = request.StablePrefix,
                    ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                }
            };
        }
        else
        {
            systemContent = JsonValue.Create(request.StablePrefix)!;
        }

        var payload = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = systemContent
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = request.VariableContent
                }
            },
            ["max_tokens"] = request.MaxOutputTokens,
            ["stream"] = false
        };

        if (sendTemperature)
        {
            payload["temperature"] = request.Temperature;
        }

        return payload.ToJsonString(RequestJsonOptions);
    }

    private static bool IsOpenRouterCacheMode(string? cacheMode) =>
        string.Equals(cacheMode, "openrouter", StringComparison.OrdinalIgnoreCase);

    private static bool IsNoneCacheMode(string? cacheMode) =>
        string.IsNullOrWhiteSpace(cacheMode)
        || string.Equals(cacheMode, "none", StringComparison.OrdinalIgnoreCase);

    private static LlmResponse ParseResponse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LlmException("LLM returned a response that is not valid JSON.", isRetryable: false, innerException: ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                throw new LlmException("LLM returned no choices.", isRetryable: false);
            }

            var choice = choices[0];
            var content = string.Empty;
            if (choice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var contentElement))
            {
                content = ReadContent(contentElement);
            }

            var finishReason = string.Empty;
            if (choice.TryGetProperty("finish_reason", out var finish)
                && finish.ValueKind == JsonValueKind.String)
            {
                finishReason = finish.GetString() ?? string.Empty;
            }

            var promptTokens = 0;
            int? cachedTokens = null;
            int? completionTokens = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var promptElement)
                    && promptElement.TryGetInt32(out var parsedPrompt))
                {
                    promptTokens = parsedPrompt;
                }

                cachedTokens = ReadCachedTokens(usage);

                if (usage.TryGetProperty("completion_tokens", out var completionElement)
                    && completionElement.TryGetInt32(out var parsedCompletion))
                {
                    completionTokens = parsedCompletion;
                }
            }

            return new LlmResponse
            {
                Content = content,
                FinishReason = finishReason,
                PromptTokens = promptTokens,
                CachedTokens = cachedTokens,
                CompletionTokens = completionTokens
            };
        }
    }

    private static int? ReadCachedTokens(JsonElement usage)
    {
        if (usage.TryGetProperty("prompt_tokens_details", out var details)
            && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("cached_tokens", out var nested)
            && nested.TryGetInt32(out var nestedValue))
        {
            return nestedValue;
        }

        if (usage.TryGetProperty("cached_tokens", out var flat)
            && flat.TryGetInt32(out var flatValue))
        {
            return flatValue;
        }

        return null;
    }

    private static string ReadContent(JsonElement contentElement)
    {
        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString() ?? string.Empty;
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in contentElement.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                builder.Append(part.GetString());
                continue;
            }

            if (part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }
    internal static string FormatGatewayError(int status, string? body, bool credentialsRejected)
    {
        var snippet = CompactErrorBody(body);
        if (credentialsRejected)
        {
            var message =
                $"LLM rejected the API key or gateway credentials (HTTP {status}). Check Llm:ApiKey and Llm:BaseUrl in appsettings.Local.json.";
            return string.IsNullOrEmpty(snippet) ? message : message + " " + snippet;
        }

        if (string.IsNullOrEmpty(snippet))
        {
            return $"LLM gateway error (HTTP {status}).";
        }

        return $"LLM gateway error (HTTP {status}): {snippet}";
    }
    internal static string CompactErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(body.Length, GatewayErrorBodyLimit));
        var previousWhitespace = false;
        foreach (var ch in body)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (!previousWhitespace && builder.Length > 0)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWhitespace = false;
            if (builder.Length >= GatewayErrorBodyLimit)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }
}
