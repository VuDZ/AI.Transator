namespace Ai.Translator.Core.Domain;

public sealed class ModelProfile
{
    public required int ContextWindowTokens { get; init; }

    public required int ReservedOutputTokens { get; init; }
}
