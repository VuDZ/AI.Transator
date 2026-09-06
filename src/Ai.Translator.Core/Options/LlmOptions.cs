namespace Ai.Translator.Core.Options;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int ContextWindowTokens { get; set; }

    public int ReservedOutputTokens { get; set; }

    public string CacheMode { get; set; } = "none";
}
