namespace Ai.Translator.Core.Domain;

public sealed class LlmUsageSnapshot
{
    public int PromptTokens { get; init; }

    public int? CachedTokens { get; init; }

    public int? CompletionTokens { get; init; }

    public TimeSpan Elapsed { get; init; }

    public int StepCount { get; init; }

    public int FailedCount { get; init; }
}
