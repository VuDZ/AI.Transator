using System.Text;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Translation;

public sealed class ChapterChunker : IChapterChunker
{
    public const string BudgetExceededMessage =
        "The working glossary is too large (or the model context window is too small) to fit the stable prefix and a single HTML block.";

    private readonly ITokenEstimator _estimator;

    public ChapterChunker(ITokenEstimator estimator)
    {
        ArgumentNullException.ThrowIfNull(estimator);
        _estimator = estimator;
    }

    public IReadOnlyList<TranslationChunk> Chunk(
        EpubChapter chapter,
        int prefixTokenCount,
        ModelProfile modelProfile)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(chapter.FilePath);
        ArgumentNullException.ThrowIfNull(modelProfile);

        var budget = modelProfile.GetSourceBudget(prefixTokenCount);
        if (budget <= 0)
        {
            throw new InvalidOperationException(BudgetExceededMessage);
        }

        var fragments = chapter.BlockFragments;
        if (fragments is null || fragments.Count == 0)
        {
            var fallback = chapter.BodyInnerHtml ?? string.Empty;
            fragments = string.IsNullOrWhiteSpace(fallback) ? [] : [fallback];
        }

        if (fragments.Count == 0)
        {
            return
            [
                new TranslationChunk
                {
                    Id = string.Empty,
                    ChapterFilePath = chapter.FilePath,
                    ChapterNumber = 0,
                    SourceHtml = chapter.BodyInnerHtml ?? string.Empty
                }
            ];
        }

        var packed = new List<List<string>>();
        var current = new List<string>();
        var currentHtml = new StringBuilder();
        var currentTokens = 0;
        var useSeparateBudgets = modelProfile.MaxInputTokens is not null
            || modelProfile.TranslationOutputTokenMultiplier > 0 || modelProfile.ReasoningTokenReserve > 0;
        foreach (var fragment in fragments)
        {
            ArgumentNullException.ThrowIfNull(fragment);
            var fragmentTokens = _estimator.Estimate(fragment);
            if (fragmentTokens > budget)
            {
                throw new InvalidOperationException(BudgetExceededMessage);
            }

            currentHtml.Append(fragment);
            var combinedTokens = useSeparateBudgets
                ? _estimator.Estimate(currentHtml.ToString())
                : currentTokens + fragmentTokens;
            if (current.Count > 0 && combinedTokens > budget)
            {
                packed.Add(current);
                current = [fragment];
                currentHtml.Clear();
                currentHtml.Append(fragment);
                currentTokens = fragmentTokens;
                continue;
            }

            current.Add(fragment);
            currentTokens = combinedTokens;

        }

        if (current.Count > 0)
        {
            packed.Add(current);
        }

        var chunks = new List<TranslationChunk>(packed.Count);
        foreach (var group in packed)
        {
            chunks.Add(new TranslationChunk
            {
                Id = string.Empty,
                ChapterFilePath = chapter.FilePath,
                ChapterNumber = 0,
                SourceHtml = JoinFragments(group)
            });
        }

        return chunks;
    }

    private static string JoinFragments(List<string> fragments)
    {
        if (fragments.Count == 1)
        {
            return fragments[0];
        }

        var builder = new StringBuilder();
        foreach (var fragment in fragments)
        {
            builder.Append(fragment);
        }

        return builder.ToString();
    }
}
