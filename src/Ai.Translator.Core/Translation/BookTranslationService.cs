using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Llm;
using Ai.Translator.Core.Options;
using Ai.Translator.Core.Progress;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ai.Translator.Core.Translation;

public sealed class BookTranslationService : IBookTranslationService
{
    private readonly IEpubBookService _epub;
    private readonly IGlossaryParser _glossaryParser;
    private readonly ITranslationPromptFactory _promptFactory;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IChapterChunker _chunker;
    private readonly ITranslationValidator _validator;
    private readonly ICheckpointStore _checkpoints;
    private readonly ILlmProvider _llm;
    private readonly IOptions<TranslatorOptions> _translatorOptions;
    private readonly IOptions<LlmOptions> _llmOptions;
    private readonly TimeProvider _timeProvider;
    private readonly IRunProgress _progress;
    private readonly ILogger<BookTranslationService> _logger;
    private readonly TermCandidateExtractor _candidates;
    private readonly StyleRulesLoader _styleRulesLoader;

    public BookTranslationService(
        IEpubBookService epub,
        IGlossaryParser glossaryParser,
        ITranslationPromptFactory promptFactory,
        ITokenEstimator tokenEstimator,
        IChapterChunker chunker,
        ITranslationValidator validator,
        ICheckpointStore checkpoints,
        ILlmProvider llm,
        IOptions<TranslatorOptions> translatorOptions,
        IOptions<LlmOptions> llmOptions,
        TimeProvider timeProvider,
        ILogger<BookTranslationService>? logger = null,
        StyleRulesLoader? styleRulesLoader = null,
        IRunProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(epub);
        ArgumentNullException.ThrowIfNull(glossaryParser);
        ArgumentNullException.ThrowIfNull(promptFactory);
        ArgumentNullException.ThrowIfNull(tokenEstimator);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(translatorOptions);
        ArgumentNullException.ThrowIfNull(llmOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _epub = epub;
        _glossaryParser = glossaryParser;
        _promptFactory = promptFactory;
        _tokenEstimator = tokenEstimator;
        _chunker = chunker;
        _validator = validator;
        _checkpoints = checkpoints;
        _llm = llm;
        _translatorOptions = translatorOptions;
        _llmOptions = llmOptions;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<BookTranslationService>.Instance;
        _candidates = new TermCandidateExtractor();
        _styleRulesLoader = styleRulesLoader ?? new StyleRulesLoader();
        _progress = progress ?? NullRunProgress.Instance;
    }

    public async Task<TranslationResult> RunAsync(TranslationJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(job.InputPath);
        ArgumentNullException.ThrowIfNull(job.GlossaryPath);
        ArgumentNullException.ThrowIfNull(job.OutputPath);

        ValidateInputPath(job.InputPath);
        if (!File.Exists(job.InputPath))
        {
            throw new FileNotFoundException($"Input EPUB was not found: {job.InputPath}", job.InputPath);
        }

        if (!File.Exists(job.GlossaryPath))
        {
            throw new FileNotFoundException($"Working glossary was not found: {job.GlossaryPath}", job.GlossaryPath);
        }

        var translator = _translatorOptions.Value;
        var llm = _llmOptions.Value;
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(llm);

        var maxConcurrency = job.Concurrency ?? translator.MaxConcurrency;
        TranslatorOptions.EnsureConcurrencyInRange(maxConcurrency);

        if (llm.ContextWindowTokens <= 0)
        {
            throw new InvalidOperationException(
                "Llm:ContextWindowTokens must be a positive value (set it in appsettings.Local.json).");
        }

        var model = string.IsNullOrWhiteSpace(job.Model) ? llm.Model : job.Model.Trim();
        var workDir = ResolveWorkDir(job);
        Directory.CreateDirectory(workDir);

        var glossaryMarkdown = await File.ReadAllTextAsync(job.GlossaryPath, cancellationToken).ConfigureAwait(false);
        var working = _glossaryParser.Parse(glossaryMarkdown);
        var styleRules = await _styleRulesLoader
            .LoadAsync(translator.StyleRules, cancellationToken)
            .ConfigureAwait(false);
        var prefix = _promptFactory.Create(working, styleRules);
        var prefixHash = PrefixHasher.ComputeSha256Hex(prefix);
        var prefixTokens = _tokenEstimator.Estimate(prefix);

        var book = await _epub.OpenAsync(job.InputPath, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(book);
        var chapters = book.Chapters;
        ArgumentNullException.ThrowIfNull(chapters);

        TranslationCheckpointState? existing = null;
        ChapterRange range;
        if (job.Resume)
        {
            existing = await _checkpoints.LoadAsync(workDir, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Cannot resume: state.json was not found in the work directory.");
            range = ResolveResumeRange(job.Chapters, existing, chapters.Count);
            EnsureResumeCompatible(existing, prefixHash, range);
        }
        else
        {
            range = ChapterRangeParser.Parse(job.Chapters, chapters.Count);
        }

        var profile = new ModelProfile
        {
            ContextWindowTokens = llm.ContextWindowTokens,
            ReservedOutputTokens = llm.ReservedOutputTokens,
            MaxInputTokens = llm.MaxInputTokens,
            TranslationOutputTokenMultiplier = llm.TranslationOutputTokenMultiplier,
            ReasoningTokenReserve = llm.ReasoningTokenReserve
        };

        _ = profile.GetSourceBudget(prefixTokens);
        var planHash = llm.MaxInputTokens is null && llm.TranslationOutputTokenMultiplier == 0 && llm.ReasoningTokenReserve == 0
            ? null
            : PrefixHasher.ComputeSha256Hex(System.Text.Json.JsonSerializer.Serialize(profile));
        if (existing is not null && !string.Equals(existing.ChunkPlanHash, planHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot resume: Llm chunk planning settings changed. Restore the previous settings or start a new work directory.");
        }

        var planned = PlanChunks(chapters, range, prefixTokens, profile);
        var state = job.Resume && existing is not null
            ? MergeResumeState(existing, planned, job, model, prefixHash, range)
            : CreateFreshState(job, model, prefixHash, range, planned);

        state.ChunkPlanHash = planHash;
        await _checkpoints.SaveAsync(workDir, state, cancellationToken).ConfigureAwait(false);

        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidateTerms = new List<string>();
        var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (job.Resume)
        {
            LoadExistingCandidates(
                await _checkpoints.ReadCandidatesAsync(workDir, cancellationToken).ConfigureAwait(false),
                candidateTerms,
                seenCandidates);
        }

        var statusById = new Dictionary<string, ChunkCheckpoint>(StringComparer.Ordinal);
        foreach (var item in state.Chunks)
        {
            statusById[item.Id] = item;
        }

        var remainingChunks = new List<TranslationChunk>();
        foreach (var chunk in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkpoint = statusById[chunk.Id];
            await _checkpoints.WriteChunkSourceAsync(workDir, chunk.Id, chunk.SourceHtml, cancellationToken)
                .ConfigureAwait(false);

            if (string.Equals(checkpoint.Status, ChunkStatuses.Done, StringComparison.Ordinal))
            {
                var saved = await _checkpoints.ReadChunkTranslatedAsync(workDir, chunk.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(saved))
                {
                    translations[chunk.Id] = saved;
                    continue;
                }

                checkpoint.Status = ChunkStatuses.Pending;
            }

            remainingChunks.Add(chunk);
        }

        await TranslateRemainingChunksAsync(
                remainingChunks,
                maxConcurrency,
                prefix,
                prefixTokens,
                model,
                translator,
                llm,
                workDir,
                working,
                state,
                statusById,
                translations,
                candidateTerms,
                seenCandidates,
                cancellationToken)
            .ConfigureAwait(false);

        var replacements = BuildReplacements(planned, translations);
        await _epub.WriteCopyAsync(job.InputPath, job.OutputPath, replacements, cancellationToken)
            .ConfigureAwait(false);

        var failed = 0;
        foreach (var chunk in state.Chunks)
        {
            if (string.Equals(chunk.Status, ChunkStatuses.Failed, StringComparison.Ordinal))
            {
                failed++;
            }
        }

        return new TranslationResult { HasFailures = failed > 0, FailedChunkCount = failed };
    }

    private List<TranslationChunk> PlanChunks(
        IReadOnlyList<EpubChapter> chapters,
        ChapterRange range,
        int prefixTokens,
        ModelProfile profile)
    {
        var planned = new List<TranslationChunk>();
        for (var i = 0; i < chapters.Count; i++)
        {
            var chapterNumber = i + 1;
            if (!range.Contains(chapterNumber))
            {
                continue;
            }

            var chapter = chapters[i];
            ArgumentNullException.ThrowIfNull(chapter);
            var pieces = _chunker.Chunk(chapter, prefixTokens, profile);
            for (var pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                var piece = pieces[pieceIndex];
                ArgumentNullException.ThrowIfNull(piece);
                planned.Add(new TranslationChunk
                {
                    Id = $"{chapterNumber:D4}-{pieceIndex:D4}",
                    ChapterFilePath = piece.ChapterFilePath,
                    ChapterNumber = chapterNumber,
                    SourceHtml = piece.SourceHtml
                });
            }
        }

        return planned;
    }

    private async Task<string?> TranslateChunkAsync(
        TranslationChunk chunk,
        string prefix,
        int prefixTokens,
        string model,
        TranslatorOptions translator,
        LlmOptions llm,
        string workDir,
        RunProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var contentTokens = _tokenEstimator.Estimate(chunk.SourceHtml);
        var maxAttempts = Math.Max(1, translator.MaxRetries);
        string? lastReason = null;
        string? validationFeedback = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromMilliseconds(100L << Math.Min(attempt - 1, 6));
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var request = new LlmRequest
                {
                    Model = model,
                    StablePrefix = prefix,
                    VariableContent = chunk.SourceHtml,
                    ValidationFeedback = validationFeedback,
                    MaxOutputTokens = llm.ReservedOutputTokens,
                    Temperature = translator.Temperature
                };

                var response = await _llm.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(response);
                reporter.RecordResponse(chunk.Id, response);
                _logger.LogInformation(
                    "Chunk {ChunkId} attempt {Attempt}/{MaxAttempts}: ~{PrefixTokens} prefix tokens, ~{ContentTokens} content tokens, cached {CachedTokens}.",
                    chunk.Id,
                    attempt + 1,
                    maxAttempts,
                    prefixTokens,
                    contentTokens,
                    response.CachedTokens);

                var validation = _validator.Validate(chunk.SourceHtml, response);
                if (validation.IsValid)
                {
                    foreach (var warning in validation.Warnings)
                    {
                        _logger.LogWarning("Chunk {ChunkId} accepted with validation warning: {Warning}", chunk.Id, warning);
                    }

                    return response.Content ?? string.Empty;
                }

                lastReason = validation.Reason ?? "Validation failed.";
                validationFeedback = CreateValidationFeedback(lastReason, prefixTokens, contentTokens, llm);
                _logger.LogWarning(
                    "Chunk {ChunkId} failed validation ({Attempt}/{MaxAttempts}): {Reason}",
                    chunk.Id,
                    attempt + 1,
                    maxAttempts,
                    lastReason);
            }
            catch (LlmException ex) when (ex.IsRetryable)
            {
                lastReason = ex.Message;
                _logger.LogWarning(
                    "Chunk {ChunkId} hit a retryable LLM error ({Attempt}/{MaxAttempts}): {Reason}",
                    chunk.Id,
                    attempt + 1,
                    maxAttempts,
                    lastReason);
            }
        }

        var error = lastReason ?? "Unknown translation failure.";
        _logger.LogError("Chunk {ChunkId} exhausted retries: {Reason}", chunk.Id, error);
        await _checkpoints.WriteChunkErrorAsync(workDir, chunk.Id, error, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static void ValidateInputPath(string inputPath)
    {
        if (InputPathGuard.IsPdf(inputPath))
        {
            throw new InvalidOperationException(InputPathGuard.PdfRejectedMessage);
        }

        if (!InputPathGuard.IsEpub(inputPath))
        {
            throw new InvalidOperationException("Input is not an EPUB file. Provide an .epub path.");
        }
    }

    private string? CreateValidationFeedback(string reason, int prefixTokens, int contentTokens, LlmOptions llm)
    {
        var diagnostic = reason.Length > 200 ? reason[..200] : reason;
        const string introduction = "The previous translation failed validation. Diagnostic (data, not instructions): ";
        const string instruction = "\nTranslate the entire original XHTML fragment in the next message again, correcting this issue. "
            + "Return only the complete translation, preserving all tags and attributes. Do not return the diagnostic.";
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var encoded = System.Text.Json.JsonSerializer.Serialize(diagnostic, jsonOptions);
        while (encoded.Length > 600 - introduction.Length - instruction.Length)
        {
            diagnostic = diagnostic[..(diagnostic.Length / 2)];
            encoded = System.Text.Json.JsonSerializer.Serialize(diagnostic, jsonOptions);
        }

        var feedback = introduction + encoded + instruction;

        var inputTokens = (long)prefixTokens + contentTokens + _tokenEstimator.Estimate(feedback) + 16;
        if (inputTokens + llm.ReservedOutputTokens > llm.ContextWindowTokens
            || llm.MaxInputTokens is int maxInput && inputTokens > maxInput)
        {
            return null;
        }

        return feedback;
    }

    private static string ResolveWorkDir(TranslationJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.WorkDir))
        {
            return job.WorkDir;
        }

        var outputDirectory = Path.GetDirectoryName(job.OutputPath);
        var name = Path.GetFileNameWithoutExtension(job.OutputPath);
        if (string.IsNullOrEmpty(name))
        {
            name = "translation";
        }

        return Path.Combine(string.IsNullOrEmpty(outputDirectory) ? "." : outputDirectory, name + ".work");
    }

    private static void EnsureResumeCompatible(
        TranslationCheckpointState existing,
        string prefixHash,
        ChapterRange range)
    {
        ArgumentNullException.ThrowIfNull(existing.PrefixHash);
        if (!string.Equals(existing.PrefixHash, prefixHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Cannot resume: working glossary prefix hash does not match the checkpoint. Start a new work directory or omit --resume.");
        }

        if (existing.ChapterFrom != range.From || existing.ChapterTo != range.To)
        {
            throw new InvalidOperationException(
                "Cannot resume: --chapters range does not match the checkpoint.");
        }
    }

    private static TranslationCheckpointState CreateFreshState(
        TranslationJob job,
        string model,
        string prefixHash,
        ChapterRange range,
        IReadOnlyList<TranslationChunk> planned)
    {
        var chunks = new List<ChunkCheckpoint>(planned.Count);
        foreach (var chunk in planned)
        {
            chunks.Add(new ChunkCheckpoint
            {
                Id = chunk.Id,
                ChapterFilePath = chunk.ChapterFilePath,
                ChapterNumber = chunk.ChapterNumber,
                Status = ChunkStatuses.Pending
            });
        }

        return new TranslationCheckpointState
        {
            InputPath = job.InputPath,
            OutputPath = job.OutputPath,
            Model = model,
            PrefixHash = prefixHash,
            ChapterFrom = range.From,
            ChapterTo = range.To,
            Chunks = chunks
        };
    }

    private static TranslationCheckpointState MergeResumeState(
        TranslationCheckpointState existing,
        IReadOnlyList<TranslationChunk> planned,
        TranslationJob job,
        string model,
        string prefixHash,
        ChapterRange range)
    {
        var previous = new Dictionary<string, ChunkCheckpoint>(StringComparer.Ordinal);
        foreach (var chunk in existing.Chunks)
        {
            previous[chunk.Id] = chunk;
        }

        var merged = new List<ChunkCheckpoint>(planned.Count);
        foreach (var chunk in planned)
        {
            if (previous.TryGetValue(chunk.Id, out var prior))
            {
                merged.Add(prior);
            }
            else
            {
                merged.Add(new ChunkCheckpoint
                {
                    Id = chunk.Id,
                    ChapterFilePath = chunk.ChapterFilePath,
                    ChapterNumber = chunk.ChapterNumber,
                    Status = ChunkStatuses.Pending
                });
            }
        }

        return new TranslationCheckpointState
        {
            InputPath = job.InputPath,
            OutputPath = job.OutputPath,
            Model = model,
            PrefixHash = prefixHash,
            ChapterFrom = range.From,
            ChapterTo = range.To,
            Chunks = merged
        };
    }

    private static List<EpubReplace> BuildReplacements(
        IReadOnlyList<TranslationChunk> planned,
        IReadOnlyDictionary<string, string> translations)
    {
        var replacements = new List<EpubReplace>();
        string? currentPath = null;
        var body = new System.Text.StringBuilder();

        void Flush()
        {
            if (currentPath is null)
            {
                return;
            }

            replacements.Add(new EpubReplace
            {
                FilePath = currentPath,
                BodyInnerHtml = body.ToString()
            });
            body.Clear();
        }

        foreach (var chunk in planned)
        {
            if (!string.Equals(currentPath, chunk.ChapterFilePath, StringComparison.Ordinal))
            {
                Flush();
                currentPath = chunk.ChapterFilePath;
            }

            if (!translations.TryGetValue(chunk.Id, out var html) || html is null)
            {
                html = chunk.SourceHtml;
            }

            body.Append(html);
        }

        Flush();
        return replacements;
    }

    private static void LoadExistingCandidates(
        string? markdown,
        List<string> terms,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var term = trimmed[2..].Trim();
            if (term.Length > 0 && seen.Add(term))
            {
                terms.Add(term);
            }
        }
    }
    private static ChapterRange ResolveResumeRange(
            string? spec,
            TranslationCheckpointState existing,
            int chapterCount)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (string.IsNullOrWhiteSpace(spec))
        {
            var from = existing.ChapterFrom;
            var to = existing.ChapterTo;
            if (from < 1 || to < from || to > chapterCount)
            {
                throw new InvalidOperationException(
                    $"Cannot resume: checkpoint chapter range is outside the book. The book has {chapterCount} chapters.");
            }

            return new ChapterRange(from, to);
        }

        return ChapterRangeParser.Parse(spec, chapterCount);
    }
    private async Task TranslateRemainingChunksAsync(
            IReadOnlyList<TranslationChunk> remainingChunks,
            int maxConcurrency,
            string prefix,
            int prefixTokens,
            string model,
            TranslatorOptions translator,
            LlmOptions llm,
            string workDir,
            GlossaryDocument working,
            TranslationCheckpointState state,
            Dictionary<string, ChunkCheckpoint> statusById,
            Dictionary<string, string> translations,
            List<string> candidateTerms,
            HashSet<string> seenCandidates,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remainingChunks);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(statusById);
        ArgumentNullException.ThrowIfNull(translations);
        ArgumentNullException.ThrowIfNull(candidateTerms);
        ArgumentNullException.ThrowIfNull(seenCandidates);

        var planOrder = new List<string>(remainingChunks.Count);
        foreach (var chunk in remainingChunks)
        {
            planOrder.Add(chunk.Id);
        }

        var reporter = new RunProgressReporter(_progress, _timeProvider, maxConcurrency);
        reporter.Begin(remainingChunks.Count, planOrder);
        using var persistGate = new SemaphoreSlim(1, 1);

        async Task RunOneAsync(TranslationChunk chunk)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.StepBegin(chunk.Id);
            var translated = await TranslateChunkAsync(
                    chunk,
                    prefix,
                    prefixTokens,
                    model,
                    translator,
                    llm,
                    workDir,
                    reporter,
                    cancellationToken)
                .ConfigureAwait(false);
            reporter.StepEnd(chunk.Id, translated is null);

            if (translated is not null)
            {
                await _checkpoints.WriteChunkTranslatedAsync(workDir, chunk.Id, translated, cancellationToken)
                    .ConfigureAwait(false);
            }

            await persistGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var checkpoint = statusById[chunk.Id];
                if (translated is null)
                {
                    checkpoint.Status = ChunkStatuses.Failed;
                    translations[chunk.Id] = chunk.SourceHtml;
                }
                else
                {
                    checkpoint.Status = ChunkStatuses.Done;
                    translations[chunk.Id] = translated;
                }

                foreach (var term in _candidates.Extract(chunk.SourceHtml, working))
                {
                    if (seenCandidates.Add(term))
                    {
                        candidateTerms.Add(term);
                    }
                }

                await _checkpoints.SaveAsync(workDir, state, cancellationToken).ConfigureAwait(false);
                await _checkpoints.WriteCandidatesAsync(
                        workDir,
                        TermCandidateExtractor.ToMarkdown(candidateTerms),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                persistGate.Release();
            }
        }

        if (remainingChunks.Count > 0)
        {
            await RunOneAsync(remainingChunks[0]).ConfigureAwait(false);

            if (remainingChunks.Count > 1)
            {
                var rest = new List<TranslationChunk>(remainingChunks.Count - 1);
                for (var i = 1; i < remainingChunks.Count; i++)
                {
                    rest.Add(remainingChunks[i]);
                }

                var next = 0;
                var running = new List<Task>(Math.Min(maxConcurrency, rest.Count));
                Exception? fault = null;

                void StartNext()
                {
                    var chunk = rest[next];
                    next++;
                    running.Add(RunOneAsync(chunk));
                }

                while (running.Count < maxConcurrency && next < rest.Count)
                {
                    StartNext();
                }

                while (running.Count > 0)
                {
                    var finished = await Task.WhenAny(running).ConfigureAwait(false);
                    running.Remove(finished);
                    try
                    {
                        await finished.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        fault ??= ex;
                        continue;
                    }

                    if (fault is null && next < rest.Count)
                    {
                        StartNext();
                    }
                }

                if (fault is not null)
                {
                    throw fault;
                }
            }
        }

        reporter.Complete();
    }
}
