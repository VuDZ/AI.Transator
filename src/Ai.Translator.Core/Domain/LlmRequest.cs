namespace Ai.Translator.Core.Domain;

public sealed class LlmRequest
{
    public required string Model { get; init; }

    public required string StablePrefix { get; init; }

    public required string VariableContent { get; init; }

    public string? ValidationFeedback { get; init; }

    public int MaxOutputTokens { get; init; }

    public double Temperature { get; init; }
}
