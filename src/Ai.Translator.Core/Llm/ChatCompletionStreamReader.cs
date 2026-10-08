using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ai.Translator.Core.Llm;

internal static class ChatCompletionStreamReader
{
    public static async Task<string> ReadAsync(HttpContent httpContent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContent);
        await using var stream = await httpContent.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        var content = new StringBuilder();
        string? finishReason = null;
        JsonNode? usage = null;
        var done = false;

        bool ProcessEvent()
        {
            if (data.Length == 0)
            {
                return false;
            }

            var payload = data.ToString();
            data.Clear();
            if (payload.Trim() == "[DONE]")
            {
                done = true;
                return true;
            }

            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Expected an SSE JSON object.");
                }

                ThrowIfError(root);
                if (root.TryGetProperty("usage", out var usageElement)
                    && usageElement.ValueKind == JsonValueKind.Object)
                {
                    usage = JsonNode.Parse(usageElement.GetRawText());
                }

                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    foreach (var choice in choices.EnumerateArray())
                    {
                        if (choice.TryGetProperty("index", out var index) && index.GetInt32() != 0)
                        {
                            continue;
                        }

                        if (choice.TryGetProperty("delta", out var delta)
                            && delta.ValueKind == JsonValueKind.Object
                            && delta.TryGetProperty("content", out var text)
                            && text.ValueKind == JsonValueKind.String)
                        {
                            content.Append(text.GetString());
                        }

                        if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
                        {
                            finishReason = finish.GetString();
                            if (finishReason == "error")
                            {
                                throw new LlmException("The LLM stream ended with an error.", isRetryable: true);
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new LlmException("The LLM stream contained invalid JSON.", isRetryable: true, innerException: ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new LlmException("The LLM stream contained an invalid chunk.", isRetryable: true, innerException: ex);
            }
            catch (FormatException ex)
            {
                throw new LlmException("The LLM stream contained an invalid chunk.", isRetryable: true, innerException: ex);
            }

            return false;
        }

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            if (line.Length == 0)
            {
                if (ProcessEvent())
                {
                    break;
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var field = line[5..];
                if (field.StartsWith(' '))
                {
                    field = field[1..];
                }

                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(field);
            }
        }

        if (!done)
        {
            ProcessEvent();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!done || string.IsNullOrWhiteSpace(finishReason))
        {
            throw new LlmException("The LLM stream ended before [DONE] and a finish reason were received.", isRetryable: true);
        }

        return new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["message"] = new JsonObject { ["content"] = content.ToString() },
                    ["finish_reason"] = finishReason
                }
            },
            ["usage"] = usage
        }.ToJsonString();
    }

    internal static void ThrowIfError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        int? code = null;
        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var codeElement))
        {
            if (codeElement.ValueKind == JsonValueKind.Number && codeElement.TryGetInt32(out var number))
            {
                code = number;
            }
            else if (codeElement.ValueKind == JsonValueKind.String && int.TryParse(codeElement.GetString(), out number))
            {
                code = number;
            }
        }

        var retryable = code is null || code == 429 || code >= 500;
        var snippet = ChatCompletionsLlmProvider.CompactErrorBody(error.GetRawText());
        throw new LlmException($"LLM generation error: {snippet}", retryable, httpStatusCode: code);
    }
}
