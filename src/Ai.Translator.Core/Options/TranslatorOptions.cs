namespace Ai.Translator.Core.Options;

public sealed class TranslatorOptions
{
    public const string SectionName = "Translator";

    public const int MinConcurrency = 1;

    public const int MaxAllowedConcurrency = 8;

    public string TargetLanguage { get; set; } = "ru";

    public int MaxRetries { get; set; } = 3;

    public double Temperature { get; set; } = 0.3;

    public string StyleRules { get; set; } = string.Empty;

    public int MaxConcurrency { get; set; } = 2;

    public static void EnsureConcurrencyInRange(int concurrency)
    {
        if (concurrency < MinConcurrency || concurrency > MaxAllowedConcurrency)
        {
            throw new InvalidOperationException(
                $"Concurrency must be between {MinConcurrency} and {MaxAllowedConcurrency}.");
        }
    }
}
