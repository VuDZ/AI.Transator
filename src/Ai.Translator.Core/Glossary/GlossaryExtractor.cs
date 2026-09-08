using System.Text;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Options;
using Ai.Translator.Core.Progress;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ai.Translator.Core.Llm;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryExtractor : IGlossaryExtractor
{
    public const string PrefixTooLargeMessage =
        "The extract prefix (rules + known terms) is too large for the model context window.";

    public const string PairTooLargeMessage =
        "A chapter pair is too large to fit the model context window.";

    public const string ResumeStateMissingMessage =
        "Cannot resume: state.json was not found in the work directory.";

    public const string ResumeMismatchMessage =
        "Cannot resume: extract inputs do not match the checkpoint. Start a new work directory or omit --resume.";

    public const string ResumeOutputMissingMessage =
        "Cannot resume: extracted Markdown was not found at the output path.";

    private readonly ILlmProvider _llm;
    private readonly IGlossaryParser _parser;
    private readonly IGlossaryWriter _writer;
    private readonly IGlossaryMerger _merger;
    private readonly ITokenEstimator _estimator;
    private readonly ExtractRulesLoader _extractRulesLoader;
    private readonly IOptions<TranslatorOptions> _translatorOptions;
    private readonly IOptions<LlmOptions> _llmOptions;
    private readonly TimeProvider _timeProvider;
    private readonly IRunProgress _progress;
    private readonly ILogger<GlossaryExtractor> _logger;
    private readonly IExtractCheckpointStore _checkpoints;

    public GlossaryExtractor(
        ILlmProvider llm,
        IGlossaryParser parser,
        IGlossaryWriter writer,
        IGlossaryMerger merger,
        ITokenEstimator estimator,
        ExtractRulesLoader extractRulesLoader,
        IOptions<TranslatorOptions> translatorOptions,
        IOptions<LlmOptions> llmOptions,
        ILogger<GlossaryExtractor>? logger = null,
        TimeProvider? timeProvider = null,
        IRunProgress? progress = null,
        IExtractCheckpointStore? checkpoints = null)
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
        _timeProvider = timeProvider ?? TimeProvider.System;
        _progress = progress ?? NullRunProgress.Instance;
        _checkpoints = checkpoints ?? new FileExtractCheckpointStore();
    }

    public async Task<GlossaryDocument> ExtractAsync(
        EpubBookModel original,
        EpubBookModel translation,
        GlossaryDocument? existingCorpus,
        string? model,
        IReadOnlyList<PairMapEntry>? pairMap,
        int? maxPairs,
        CancellationToken cancellationToken,
        ExtractRunContext? run = null)
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
        var pairs = ResolvePairs(originalChapters, translationChapters, pairMap, maxPairs);

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

        var steps = new List<(int PairIndex, EpubChapter Original, EpubChapter Translation, string OriginalFragment, string TranslationFragment, string Label)>();
        for (var i = 0; i < pairs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (originalChapter, translationChapter) = pairs[i];
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

            for (var fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                var fragment = fragments[fragmentIndex];
                var path = originalChapter.FilePath ?? string.Empty;
                var label = fragments.Count == 1
                    ? $"{i + 1}:{path}"
                    : $"{i + 1}:{path}#{fragmentIndex + 1}";
                steps.Add((i + 1, originalChapter, translationChapter, fragment.Original, fragment.Translation, label));
            }
        }

        var state = await PrepareCheckpointAsync(
                run,
                resolvedModel,
                llm.ContextWindowTokens,
                llm.ReservedOutputTokens,
                cancellationToken)
            .ConfigureAwait(false);
        var completed = new HashSet<string>(state?.CompletedSteps ?? [], StringComparer.Ordinal);
        if (run is not null && run.Resume && completed.Count > 0)
        {
            result = await LoadResumedDocumentAsync(run.OutputPath, cancellationToken).ConfigureAwait(false);
        }

        var remaining = new List<(int PairIndex, EpubChapter Original, EpubChapter Translation, string OriginalFragment, string TranslationFragment, string Label)>();
        foreach (var step in steps)
        {
            if (!completed.Contains(step.Label))
            {
                remaining.Add(step);
            }
        }

        var reporter = new RunProgressReporter(_progress, _timeProvider);
        reporter.Begin(remaining.Count);

        if (remaining.Count == 0 && run is not null)
        {
            await FlushAsync(result, run, state ?? CreateState(run, resolvedModel, llm.ContextWindowTokens, llm.ReservedOutputTokens), cancellationToken)
                .ConfigureAwait(false);
            reporter.Complete();
            return result;
        }

        foreach (var step in remaining)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.StepBegin(step.Label);
            var variable = WrapPair(
                step.OriginalFragment,
                step.TranslationFragment,
                step.Original.FilePath ?? string.Empty,
                step.Translation.FilePath ?? string.Empty,
                step.PairIndex,
                originalCount,
                translationCount);

            LlmResponse response;
            try
            {
                response = await CompleteWithRetriesAsync(
                        new LlmRequest
                        {
                            Model = resolvedModel,
                            StablePrefix = prefix,
                            VariableContent = variable,
                            MaxOutputTokens = llm.ReservedOutputTokens,
                            Temperature = translator.Temperature
                        },
                        step.Label,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (LlmException)
            {
                reporter.StepEnd(failed: true);
                throw;
            }

            reporter.RecordResponse(response);
            reporter.StepEnd(failed: false);
            result = MergeModelOutput(result, response.Content, step.PairIndex);
            if (run is not null)
            {
                state ??= CreateState(run, resolvedModel, llm.ContextWindowTokens, llm.ReservedOutputTokens);
                state.CompletedSteps.Add(step.Label);
                await FlushAsync(result, run, state, cancellationToken).ConfigureAwait(false);
            }
        }

        reporter.Complete();
        return result;
    }
    private IReadOnlyList<(EpubChapter Original, EpubChapter Translation)> ResolvePairs(
        IReadOnlyList<EpubChapter> originalChapters,
        IReadOnlyList<EpubChapter> translationChapters,
        IReadOnlyList<PairMapEntry>? pairMap,
        int? maxPairs)
    {
        if (pairMap is null)
        {
            if (maxPairs is not null)
            {
                throw new InvalidOperationException(GlossaryExtractArguments.MaxPairsRequiresPairsMessage);
            }

            return PairByIndex(originalChapters, translationChapters);
        }

        if (maxPairs is <= 0)
        {
            throw new InvalidOperationException(GlossaryExtractArguments.MaxPairsMustBePositiveMessage);
        }

        var expanded = ExpandPairMap(originalChapters, translationChapters, pairMap);
        if (maxPairs is int n && n < expanded.Count)
        {
            return expanded.GetRange(0, n);
        }

        return expanded;
    }

    private List<(EpubChapter Original, EpubChapter Translation)> PairByIndex(
        IReadOnlyList<EpubChapter> originalChapters,
        IReadOnlyList<EpubChapter> translationChapters)
    {
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

        var pairs = new List<(EpubChapter Original, EpubChapter Translation)>(pairedCount);
        for (var i = 0; i < pairedCount; i++)
        {
            pairs.Add((originalChapters[i], translationChapters[i]));
        }

        return pairs;
    }

    private static List<(EpubChapter Original, EpubChapter Translation)> ExpandPairMap(
        IReadOnlyList<EpubChapter> originalChapters,
        IReadOnlyList<EpubChapter> translationChapters,
        IReadOnlyList<PairMapEntry> pairMap)
    {
        var expanded = new List<(EpubChapter Original, EpubChapter Translation)>();
        var seenOriginal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTranslation = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in pairMap)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var originalSlice = SliceKept(originalChapters, entry.OriginalFrom, entry.OriginalTo);
            var translationSlice = SliceKept(translationChapters, entry.TranslationFrom, entry.TranslationTo);
            if (originalSlice.Count != translationSlice.Count)
            {
                throw new InvalidOperationException(
                    $"Range lengths do not match: {originalSlice.Count} vs {translationSlice.Count}.");
            }

            for (var i = 0; i < originalSlice.Count; i++)
            {
                var left = originalSlice[i];
                var right = translationSlice[i];
                if (!seenOriginal.Add(GlossaryPairPath.Normalize(left.FilePath)))
                {
                    throw new InvalidOperationException(
                        $"Chapter path is mapped more than once: {left.FilePath}");
                }

                if (!seenTranslation.Add(GlossaryPairPath.Normalize(right.FilePath)))
                {
                    throw new InvalidOperationException(
                        $"Chapter path is mapped more than once: {right.FilePath}");
                }

                expanded.Add((left, right));
            }
        }

        return expanded;
    }

    private static IReadOnlyList<EpubChapter> SliceKept(
        IReadOnlyList<EpubChapter> chapters,
        string fromPath,
        string toPath)
    {
        ArgumentNullException.ThrowIfNull(fromPath);
        ArgumentNullException.ThrowIfNull(toPath);

        var from = IndexOfChapter(chapters, fromPath);
        if (from < 0)
        {
            throw new InvalidOperationException($"Chapter path was not found: {fromPath}");
        }

        var to = IndexOfChapter(chapters, toPath);
        if (to < 0)
        {
            throw new InvalidOperationException($"Chapter path was not found: {toPath}");
        }

        if (from > to)
        {
            throw new InvalidOperationException(
                $"Range start '{fromPath}' is after end '{toPath}'.");
        }

        var slice = new EpubChapter[to - from + 1];
        for (var i = from; i <= to; i++)
        {
            slice[i - from] = chapters[i];
        }

        return slice;
    }

    private static int IndexOfChapter(IReadOnlyList<EpubChapter> chapters, string path)
    {
        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            ArgumentNullException.ThrowIfNull(chapter);
            if (GlossaryPairPath.AreEqual(chapter.FilePath, path))
            {
                return i;
            }
        }

        return -1;
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
    private static void EnsureResumeCompatible(
            ExtractCheckpointState existing,
            ExtractRunContext run,
            string model,
            int contextWindowTokens,
            int reservedOutputTokens)
    {
        if (!PathsEqual(existing.OriginalPath, run.OriginalPath)
            || !PathsEqual(existing.TranslationPath, run.TranslationPath)
            || !string.Equals(existing.PairsHash, run.PairsHash, StringComparison.Ordinal)
            || existing.MaxPairs != run.MaxPairs
            || !PathsEqual(existing.MergeIntoPath, run.MergeIntoPath)
            || !string.Equals(existing.Model, model, StringComparison.Ordinal)
            || existing.ContextWindowTokens != contextWindowTokens
            || existing.ReservedOutputTokens != reservedOutputTokens)
        {
            throw new InvalidOperationException(ResumeMismatchMessage);
        }
    }
    private static ExtractCheckpointState CreateState(
            ExtractRunContext run,
            string model,
            int contextWindowTokens,
            int reservedOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new ExtractCheckpointState
        {
            OriginalPath = run.OriginalPath,
            TranslationPath = run.TranslationPath,
            PairsHash = run.PairsHash,
            MaxPairs = run.MaxPairs,
            MergeIntoPath = string.IsNullOrWhiteSpace(run.MergeIntoPath) ? null : run.MergeIntoPath,
            Model = model,
            ContextWindowTokens = contextWindowTokens,
            ReservedOutputTokens = reservedOutputTokens,
            CompletedSteps = []
        };
    }
    private async Task FlushAsync(
            GlossaryDocument document,
            ExtractRunContext run,
            ExtractCheckpointState state,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(state);
        var markdown = _writer.Write(document);
        var directory = Path.GetDirectoryName(run.OutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(run.OutputPath, markdown, cancellationToken).ConfigureAwait(false);
        await _checkpoints.SaveAsync(run.WorkDir, state, cancellationToken).ConfigureAwait(false);
    }
    private async Task<LlmResponse> CompleteWithRetriesAsync(
            LlmRequest request,
            string stepLabel,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var maxAttempts = Math.Max(1, _translatorOptions.Value.MaxRetries);
        LlmException? last = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromMilliseconds(100L << Math.Min(attempt - 1, 6));
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var response = await _llm.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(response);
                return response;
            }
            catch (LlmException ex) when (ex.IsRetryable)
            {
                last = ex;
                _logger.LogWarning(
                    "Extract step {Label} hit a retryable LLM error ({Attempt}/{MaxAttempts}): {Reason}",
                    stepLabel,
                    attempt + 1,
                    maxAttempts,
                    ex.Message);
            }
        }

        throw last ?? new LlmException("Extract exhausted retries.", isRetryable: false);
    }
    private async Task<ExtractCheckpointState?> PrepareCheckpointAsync(
            ExtractRunContext? run,
            string model,
            int contextWindowTokens,
            int reservedOutputTokens,
            CancellationToken cancellationToken)
    {
        if (run is null)
        {
            return null;
        }

        if (run.Resume)
        {
            var existing = await _checkpoints.LoadAsync(run.WorkDir, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(ResumeStateMissingMessage);
            EnsureResumeCompatible(existing, run, model, contextWindowTokens, reservedOutputTokens);
            return existing;
        }

        var fresh = CreateState(run, model, contextWindowTokens, reservedOutputTokens);
        await _checkpoints.SaveAsync(run.WorkDir, fresh, cancellationToken).ConfigureAwait(false);
        return fresh;
    }
    private async Task<GlossaryDocument> LoadResumedDocumentAsync(string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException(ResumeOutputMissingMessage);
        }

        var markdown = await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false);
        return _parser.Parse(markdown);
    }
    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
