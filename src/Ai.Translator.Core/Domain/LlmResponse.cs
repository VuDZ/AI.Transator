namespace Ai.Translator.Core.Domain;

public sealed class LlmResponse
{
    public string Content { get; init; } = string.Empty;

    public string FinishReason { get; init; } = string.Empty;

    public int PromptTokens { get; init; }

    public int? CachedTokens { get; init; }
}
