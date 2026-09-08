using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Translation;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryExtractService : IGlossaryExtractService
{
    private readonly IEpubBookService _epub;
    private readonly IGlossaryParser _parser;
    private readonly IGlossaryWriter _writer;
    private readonly IGlossaryExtractor _extractor;
    private readonly IGlossaryPairMapParser _pairMapParser;

    public GlossaryExtractService(
        IEpubBookService epub,
        IGlossaryParser parser,
        IGlossaryWriter writer,
        IGlossaryExtractor extractor,
        IGlossaryPairMapParser pairMapParser)
    {
        ArgumentNullException.ThrowIfNull(epub);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(pairMapParser);
        _epub = epub;
        _parser = parser;
        _writer = writer;
        _extractor = extractor;
        _pairMapParser = pairMapParser;
    }

    public async Task ExtractAsync(
        string originalPath,
        string translationPath,
        string outputPath,
        string? mergeIntoPath,
        string? model,
        string? pairsPath,
        int? maxPairs,
        CancellationToken cancellationToken,
        string? workDir = null,
        bool resume = false)
    {
        ArgumentNullException.ThrowIfNull(originalPath);
        ArgumentNullException.ThrowIfNull(translationPath);
        ArgumentNullException.ThrowIfNull(outputPath);

        RejectIfUnsupported(originalPath, "Original");
        RejectIfUnsupported(translationPath, "Translation");

        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException($"Original EPUB was not found: {originalPath}", originalPath);
        }

        if (!File.Exists(translationPath))
        {
            throw new FileNotFoundException($"Translation EPUB was not found: {translationPath}", translationPath);
        }

        IReadOnlyList<PairMapEntry>? pairMap = null;
        string? pairsHash = null;
        if (!string.IsNullOrWhiteSpace(pairsPath))
        {
            if (!File.Exists(pairsPath))
            {
                throw new FileNotFoundException($"Pairs file was not found: {pairsPath}", pairsPath);
            }

            var pairsText = await File.ReadAllTextAsync(pairsPath, cancellationToken).ConfigureAwait(false);
            pairMap = _pairMapParser.Parse(pairsText);
            pairsHash = PrefixHasher.ComputeSha256Hex(pairsText);
        }

        GlossaryDocument? existing = null;
        if (!string.IsNullOrWhiteSpace(mergeIntoPath))
        {
            if (!File.Exists(mergeIntoPath))
            {
                throw new FileNotFoundException($"Corpus file was not found: {mergeIntoPath}", mergeIntoPath);
            }

            var corpusMarkdown = await File.ReadAllTextAsync(mergeIntoPath, cancellationToken).ConfigureAwait(false);
            existing = _parser.Parse(corpusMarkdown);
        }

        var original = await _epub.OpenAsync(originalPath, cancellationToken).ConfigureAwait(false);
        var translation = await _epub.OpenAsync(translationPath, cancellationToken).ConfigureAwait(false);
        var run = new ExtractRunContext
        {
            OutputPath = outputPath,
            WorkDir = ResolveWorkDir(outputPath, workDir),
            Resume = resume,
            OriginalPath = originalPath,
            TranslationPath = translationPath,
            PairsHash = pairsHash,
            MaxPairs = maxPairs,
            MergeIntoPath = string.IsNullOrWhiteSpace(mergeIntoPath) ? null : mergeIntoPath
        };
        var extracted = await _extractor
            .ExtractAsync(original, translation, existing, model, pairMap, maxPairs, cancellationToken, run)
            .ConfigureAwait(false);
        var output = _writer.Write(extracted);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, output, cancellationToken).ConfigureAwait(false);
    }
    private static void RejectIfUnsupported(string path, string role)
    {
        if (InputPathGuard.IsPdf(path))
        {
            throw new InvalidOperationException(InputPathGuard.PdfRejectedMessage);
        }

        if (!InputPathGuard.IsEpub(path))
        {
            throw new InvalidOperationException(
                $"{role} is not an EPUB file. Provide an .epub path.");
        }
    }
    public static string ResolveWorkDir(string outputPath, string? workDir)
    {
        if (!string.IsNullOrWhiteSpace(workDir))
        {
            return workDir;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        var name = Path.GetFileNameWithoutExtension(outputPath);
        if (string.IsNullOrEmpty(name))
        {
            name = "extract";
        }

        return Path.Combine(string.IsNullOrEmpty(outputDirectory) ? "." : outputDirectory, name + ".extract.work");
    }
}
