using System.Text;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryExtractor : IGlossaryExtractor
{
    public const string PrefixTooLargeMessage =
        "The extract prefix (rules + known terms) is too large for the model context window.";

    public const string PairTooLargeMessage =
        "A chapter pair is too large to fit the model context window.";

    private readonly ILlmProvider _llm;
    private readonly IGlossaryParser _parser;
    private readonly IGlossaryWriter _writer;
    private readonly IGlossaryMerger _merger;
    private readonly ITokenEstimator _estimator;
    private readonly ExtractRulesLoader _extractRulesLoader;
    private readonly IOptions<TranslatorOptions> _translatorOptions;
    private readonly IOptions<LlmOptions> _llmOptions;
    private readonly ILogger<GlossaryExtractor> _logger;

    public GlossaryExtractor(
        ILlmProvider llm,
        IGlossaryParser parser,
        IGlossaryWriter writer,
        IGlossaryMerger merger,
        ITokenEstimator estimator,
        ExtractRulesLoader extractRulesLoader,
        IOptions<TranslatorOptions> translatorOptions,
        IOptions<LlmOptions> llmOptions,
        ILogger<GlossaryExtractor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(merger);
        ArgumentNullException.ThrowIfNull(estimator);
        ArgumentNullException.ThrowIfNull(extractRulesLoader);
        ArgumentNullException.ThrowIfNull(translatorOptions);
        ArgumentNullException.ThrowIfNull(llmOptions);
        _llm = llm;
        _parser = parser;
        _writer = writer;
        _merger = merger;
        _estimator = estimator;
        _extractRulesLoader = extractRulesLoader;
        _translatorOptions = translatorOptions;
        _llmOptions = llmOptions;
        _logger = logger ?? NullLogger<GlossaryExtractor>.Instance;
    }

    public async Task<GlossaryDocument> ExtractAsync(
        EpubBookModel original,
        EpubBookModel translation,
        GlossaryDocument? existingCorpus,
        string? model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(translation);
        ArgumentNullException.ThrowIfNull(original.Chapters);
        ArgumentNullException.ThrowIfNull(translation.Chapters);

        var translator = _translatorOptions.Value;
        var llm = _llmOptions.Value;
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(llm);

        if (llm.ContextWindowTokens <= 0)
        {
            throw new InvalidOperationException(
                "Llm:ContextWindowTokens must be a positive value (set it in appsettings.Local.json).");
        }

        var resolvedModel = string.IsNullOrWhiteSpace(model) ? llm.Model : model.Trim();
        var originalChapters = original.Chapters;
        var translationChapters = translation.Chapters;
        var originalCount = originalChapters.Count;
        var translationCount = translationChapters.Count;
        var pairedCount = Math.Min(originalCount, translationCount);

        if (originalCount != translationCount)
        {
            _logger.LogWarning(
                "Reading order lengths differ: original has {OriginalCount} chapter(s), translation has {TranslationCount}. Pairing the first {PairedCount}; leftover chapters are not sent to the model.",
                originalCount,
                translationCount,
                pairedCount);
            LogUnpairedTail(originalChapters, pairedCount, "original");
            LogUnpairedTail(translationChapters, pairedCount, "translation");
        }

        var rules = await _extractRulesLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
        var prefix = BuildStablePrefix(
            rules,
            existingCorpus,
            llm.ContextWindowTokens,
            llm.ReservedOutputTokens);
        var prefixTokens = _estimator.Estimate(prefix);
        var budget = llm.ContextWindowTokens - prefixTokens - llm.ReservedOutputTokens;
        if (budget <= 0)
        {
            throw new InvalidOperationException(PrefixTooLargeMessage);
        }

        var result = existingCorpus ?? new GlossaryDocument
        {
            Title = "Extracted",
            Entries = []
        };

        for (var i = 0; i < pairedCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalChapter = originalChapters[i];
            var translationChapter = translationChapters[i];
            ArgumentNullException.ThrowIfNull(originalChapter);
            ArgumentNullException.ThrowIfNull(translationChapter);

            _logger.LogInformation(
                "Extract pair {Index}: original '{OriginalPath}', translation '{TranslationPath}'.",
                i + 1,
                originalChapter.FilePath,
                translationChapter.FilePath);

            var fragments = SplitPair(
                originalChapter.PlainText ?? string.Empty,
                translationChapter.PlainText ?? string.Empty,
                originalChapter.FilePath ?? string.Empty,
                translationChapter.FilePath ?? string.Empty,
                i + 1,
                originalCount,
                translationCount,
                budget);

            foreach (var fragment in fragments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var variable = WrapPair(
                    fragment.Original,
                    fragment.Translation,
                    originalChapter.FilePath ?? string.Empty,
                    translationChapter.FilePath ?? string.Empty,
                    i + 1,
                    originalCount,
                    translationCount);

                var response = await _llm.CompleteAsync(
                        new LlmRequest
                        {
                            Model = resolvedModel,
                            StablePrefix = prefix,
                            VariableContent = variable,
                            MaxOutputTokens = llm.ReservedOutputTokens,
                            Temperature = translator.Temperature
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                ArgumentNullException.ThrowIfNull(response);
                result = MergeModelOutput(result, response.Content, i + 1);
            }
        }

        return result;
    }

    private void LogUnpairedTail(IReadOnlyList<EpubChapter> chapters, int pairedCount, string side)
    {
        for (var i = pairedCount; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            ArgumentNullException.ThrowIfNull(chapter);
            _logger.LogWarning(
                "Unpaired {Side} chapter '{Path}'.",
                side,
                chapter.FilePath);
        }
    }

    private GlossaryDocument MergeModelOutput(GlossaryDocument current, string? content, int pairIndex)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Model returned empty content for pair {Index}; skipping.", pairIndex);
            return current;
        }

        try
        {
            var sliced = GlossaryParser.SliceFromFirstEntryHeading(content);
            if (sliced is null)
            {
                _logger.LogWarning(
                    "Model output for pair {Index} had no ## entries; skipping.",
                    pairIndex);
                return current;
            }

            var parsed = _parser.Parse("# Extracted\n\n" + sliced);
            return _merger.Merge(current, parsed);
        }
        catch (GlossaryFormatException ex)
        {
            _logger.LogWarning(
                ex,
                "Model output for pair {Index} was not valid glossary Markdown; skipping.",
                pairIndex);
            return current;
        }
    }

    private string BuildStablePrefix(
        string rules,
        GlossaryDocument? corpus,
        int contextWindowTokens,
        int reservedOutputTokens)
    {
        var knownFull = BuildKnownTermsSection(corpus, fullEntries: true);
        var prefix = CombinePrefix(rules, knownFull);
        if (FitsPrefix(prefix, contextWindowTokens, reservedOutputTokens))
        {
            return prefix;
        }

        var knownEnglish = BuildKnownTermsSection(corpus, fullEntries: false);
        prefix = CombinePrefix(rules, knownEnglish);
        if (FitsPrefix(prefix, contextWindowTokens, reservedOutputTokens))
        {
            return prefix;
        }

        throw new InvalidOperationException(PrefixTooLargeMessage);
    }

    private bool FitsPrefix(string prefix, int contextWindowTokens, int reservedOutputTokens)
    {
        const int minVariableTokens = 64;
        return _estimator.Estimate(prefix) + reservedOutputTokens + minVariableTokens <= contextWindowTokens;
    }

    private string BuildKnownTermsSection(GlossaryDocument? corpus, bool fullEntries)
    {
        if (corpus is null)
        {
            return string.Empty;
        }

        var entries = corpus.Entries;
        if (entries is null || entries.Count == 0)
        {
            return fullEntries ? _writer.Write(corpus) : string.Empty;
        }

        if (fullEntries)
        {
            return _writer.Write(corpus);
        }

        var builder = new StringBuilder();
        builder.Append("Already known English terms (do not duplicate unless adding a new alias):");
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (string.IsNullOrWhiteSpace(entry.English))
            {
                continue;
            }

            builder.Append('\n');
            builder.Append(entry.English);
        }

        return builder.ToString();
    }

    private static string CombinePrefix(string rules, string known)
    {
        var trimmedRules = (rules ?? string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(known))
        {
            return trimmedRules;
        }

        return trimmedRules + "\n\n" + known.TrimEnd();
    }

    private IReadOnlyList<(string Original, string Translation)> SplitPair(
        string originalText,
        string translationText,
        string originalPath,
        string translationPath,
        int index,
        int originalCount,
        int translationCount,
        int budget)
    {
        if (_estimator.Estimate(
                WrapPair(
                    originalText,
                    translationText,
                    originalPath,
                    translationPath,
                    index,
                    originalCount,
                    translationCount)) <= budget)
        {
            return [(originalText, translationText)];
        }

        var parts = 2;
        while (parts <= 1000)
        {
            var originalParts = SplitText(originalText, parts);
            var translationParts = SplitText(translationText, parts);
            var count = Math.Max(originalParts.Count, translationParts.Count);
            var result = new List<(string Original, string Translation)>(count);
            var allFit = true;
            for (var i = 0; i < count; i++)
            {
                var originalPart = i < originalParts.Count ? originalParts[i] : string.Empty;
                var translationPart = i < translationParts.Count ? translationParts[i] : string.Empty;
                var wrapped = WrapPair(
                    originalPart,
                    translationPart,
                    originalPath,
                    translationPath,
                    index,
                    originalCount,
                    translationCount);
                if (_estimator.Estimate(wrapped) > budget)
                {
                    allFit = false;
                    break;
                }

                result.Add((originalPart, translationPart));
            }

            if (allFit)
            {
                return result;
            }

            parts++;
        }

        throw new InvalidOperationException(PairTooLargeMessage);
    }

    private static List<string> SplitText(string text, int parts)
    {
        if (string.IsNullOrEmpty(text) || parts <= 1)
        {
            return [text];
        }

        var slice = (int)Math.Ceiling(text.Length / (double)parts);
        if (slice <= 0)
        {
            return [text];
        }

        var list = new List<string>();
        for (var start = 0; start < text.Length; start += slice)
        {
            var length = Math.Min(slice, text.Length - start);
            list.Add(text.Substring(start, length));
        }

        return list;
    }

    private static string WrapPair(
        string originalText,
        string translationText,
        string originalPath,
        string translationPath,
        int index,
        int originalCount,
        int translationCount)
    {
        return
            $"Original [{index}/{originalCount}] {originalPath}:\n{originalText}\n\n" +
            $"Translation [{index}/{translationCount}] {translationPath}:\n{translationText}";
    }
}
