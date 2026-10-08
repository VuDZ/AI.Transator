namespace Ai.Translator.Core.Options;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int ContextWindowTokens { get; set; }

    public int ReservedOutputTokens { get; set; }

    public int TimeoutSeconds { get; set; } = 300;

    public string CacheMode { get; set; } = "none";

    public bool SendTemperature { get; set; }

    public int? MaxInputTokens { get; set; }

    public double TranslationOutputTokenMultiplier { get; set; }

    public int ReasoningTokenReserve { get; set; }

    public bool Stream { get; set; }

    public string? ReasoningEffort { get; set; }

    public string? CacheTtl { get; set; }
}
