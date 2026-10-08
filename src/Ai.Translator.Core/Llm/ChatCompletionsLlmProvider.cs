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
        if (request.MaxOutputTokens <= 0)
        {
            throw new InvalidOperationException("The completion token limit must be positive.");
        }

        var client = _httpClientFactory.CreateClient(ServiceCollectionExtensions.LlmHttpClientName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(llm.TimeoutSeconds));
        var token = timeout.Token;
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(BuildRequestJson(request, llm), Encoding.UTF8, "application/json")
        };

        try
        {
            using var httpResponse = await client
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            var status = (int)httpResponse.StatusCode;
            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                throw new LlmException(
                    FormatGatewayError(status, body, status is 401 or 403),
                    isRetryable: status == 429 || status >= 500,
                    httpStatusCode: status);
            }

            var json = llm.Stream
                ? await ChatCompletionStreamReader.ReadAsync(httpResponse.Content, token).ConfigureAwait(false)
                : await httpResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var response = ParseResponse(json);
            _logger.LogInformation(
                "Chat Completions finished: model {Model}, finish {FinishReason}, prompt tokens {PromptTokens}, cached {CachedTokens}, completion {CompletionTokens}, reasoning {ReasoningTokens}.",
                request.Model, response.FinishReason, response.PromptTokens, response.CachedTokens,
                response.CompletionTokens, response.ReasoningTokens);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmException("The LLM request timed out.", isRetryable: true, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException("The LLM gateway is unreachable.", isRetryable: true, innerException: ex);
        }
        catch (IOException ex)
        {
            throw new LlmException("The LLM response was interrupted.", isRetryable: true, innerException: ex);
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
        if (llm.ReasoningEffort is not null && llm.ReasoningEffort is not
            ("" or "none" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max"))
        {
            throw new InvalidOperationException("Llm:ReasoningEffort must be none, minimal, low, medium, high, xhigh or max, or omitted.");
        }

        if (IsOpenRouterCacheMode(llm.CacheMode)
            && llm.CacheTtl is not null && llm.CacheTtl is not ("" or "5m" or "1h"))
        {
            throw new InvalidOperationException("Llm:CacheTtl must be 5m, 1h, or omitted.");
        }
    }

    private static string BuildRequestJson(LlmRequest request, LlmOptions llm)
    {
        JsonNode systemContent;
        if (IsOpenRouterCacheMode(llm.CacheMode))
        {
            systemContent = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = request.StablePrefix,
                    ["cache_control"] = CreateCacheControl(llm.CacheTtl)
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
            [IsOpenRouterCacheMode(llm.CacheMode) ? "max_completion_tokens" : "max_tokens"] = request.MaxOutputTokens,
            ["stream"] = llm.Stream
        };

        if (!string.IsNullOrWhiteSpace(request.ValidationFeedback))
        {
            var messages = (JsonArray)payload["messages"]!;
            messages.Insert(1, new JsonObject
            {
                ["role"] = "user",
                ["content"] = request.ValidationFeedback
            });
        }

        if (llm.SendTemperature)
        {
            payload["temperature"] = request.Temperature;
        }

        if (llm.Stream)
        {
            payload["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        if (!string.IsNullOrWhiteSpace(llm.ReasoningEffort))
        {
            payload["reasoning"] = llm.ReasoningEffort == "none"
                ? new JsonObject { ["enabled"] = false }
                : new JsonObject { ["effort"] = llm.ReasoningEffort, ["exclude"] = true };
        }

        if (IsOpenRouterCacheMode(llm.CacheMode))
        {
            payload["session_id"] = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(request.StablePrefix)));
        }

        return payload.ToJsonString(RequestJsonOptions);
    }

    private static JsonObject CreateCacheControl(string? ttl)
    {
        var cache = new JsonObject { ["type"] = "ephemeral" };
        if (!string.IsNullOrWhiteSpace(ttl))
        {
            cache["ttl"] = ttl;
        }

        return cache;
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
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new LlmException("LLM returned a response that is not an object.", isRetryable: false);
            }

            ChatCompletionStreamReader.ThrowIfError(root);
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
            int? reasoningTokens = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var promptElement)
                    && promptElement.TryGetInt32(out var parsedPrompt))
                {
                    promptTokens = parsedPrompt;
                }

                cachedTokens = ReadCachedTokens(usage);
                if (usage.TryGetProperty("completion_tokens_details", out var completionDetails)
                    && completionDetails.ValueKind == JsonValueKind.Object
                    && completionDetails.TryGetProperty("reasoning_tokens", out var reasoningElement)
                    && reasoningElement.TryGetInt32(out var parsedReasoning))
                {
                    reasoningTokens = parsedReasoning;
                }

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
                CompletionTokens = completionTokens,
                ReasoningTokens = reasoningTokens
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
                && (!part.TryGetProperty("type", out var type)
                    || type.ValueKind == JsonValueKind.String && type.GetString() == "text")
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
