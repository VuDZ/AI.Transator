namespace Ai.Translator.Core.Domain;

public sealed class TranslationResult
{
    public bool HasFailures { get; init; }

    public int FailedChunkCount { get; init; }
}
