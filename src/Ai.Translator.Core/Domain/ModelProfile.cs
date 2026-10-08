namespace Ai.Translator.Core.Domain;

public sealed class ModelProfile
{
    public required int ContextWindowTokens { get; init; }

    public required int ReservedOutputTokens { get; init; }

    public int? MaxInputTokens { get; init; }

    public double TranslationOutputTokenMultiplier { get; init; }

    public int ReasoningTokenReserve { get; init; }

    public int GetSourceBudget(int prefixTokens)
    {
        if (ContextWindowTokens <= 0 || ReservedOutputTokens <= 0
            || MaxInputTokens is <= 0 || prefixTokens < 0
            || !double.IsFinite(TranslationOutputTokenMultiplier) || TranslationOutputTokenMultiplier < 0
            || ReasoningTokenReserve < 0 || ReasoningTokenReserve >= ReservedOutputTokens)
        {
            throw new InvalidOperationException("Invalid Llm token budgets: context/output/input must be positive, multiplier finite and non-negative, and reasoning reserve smaller than output.");
        }

        var budget = (long)ContextWindowTokens - prefixTokens - ReservedOutputTokens;
        if (MaxInputTokens is int inputLimit)
        {
            budget = Math.Min(budget, (long)inputLimit - prefixTokens);
        }

        if (TranslationOutputTokenMultiplier > 0)
        {
            var outputBudget = Math.Floor((ReservedOutputTokens - ReasoningTokenReserve) / TranslationOutputTokenMultiplier);
            budget = (long)Math.Min(budget, outputBudget);
        }

        return (int)Math.Clamp(budget, int.MinValue, int.MaxValue);
    }
}
