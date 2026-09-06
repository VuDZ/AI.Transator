namespace Ai.Translator.Core.Options;

public sealed class TranslatorOptions
{
    public const string SectionName = "Translator";

    public string TargetLanguage { get; set; } = "ru";

    public int MaxRetries { get; set; } = 3;

    public double Temperature { get; set; } = 0.3;

    public string StyleRules { get; set; } = string.Empty;
}
