using System.Text;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryCompileService : IGlossaryCompileService
{
    private readonly IGlossaryParser _parser;
    private readonly IGlossaryWriter _writer;
    private readonly IGlossaryCompiler _compiler;
    private readonly IEpubBookService _epubBookService;

    public GlossaryCompileService(
        IGlossaryParser parser,
        IGlossaryWriter writer,
        IGlossaryCompiler compiler,
        IEpubBookService epubBookService)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(epubBookService);
        _parser = parser;
        _writer = writer;
        _compiler = compiler;
        _epubBookService = epubBookService;
    }

    public async Task CompileAsync(
        string corpusPath,
        string bookPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(corpusPath);
        ArgumentNullException.ThrowIfNull(bookPath);
        ArgumentNullException.ThrowIfNull(outputPath);

        if (InputPathGuard.IsPdf(bookPath))
        {
            throw new InvalidOperationException(InputPathGuard.PdfRejectedMessage);
        }

        if (!InputPathGuard.IsEpub(bookPath))
        {
            throw new InvalidOperationException("Book is not an EPUB file. Provide an .epub path.");
        }

        if (!File.Exists(corpusPath))
        {
            throw new FileNotFoundException($"Corpus file was not found: {corpusPath}", corpusPath);
        }

        if (!File.Exists(bookPath))
        {
            throw new FileNotFoundException($"Book file was not found: {bookPath}", bookPath);
        }

        var markdown = await File.ReadAllTextAsync(corpusPath, cancellationToken).ConfigureAwait(false);
        var corpus = _parser.Parse(markdown);
        if (corpus.Entries.Count == 0)
        {
            throw new GlossaryFormatException(
                "Corpus Markdown is invalid: no glossary entries (## headings).");
        }

        var book = await _epubBookService.OpenAsync(bookPath, cancellationToken).ConfigureAwait(false);
        var bookPlainText = JoinPlainText(book.Chapters);
        var working = _compiler.Compile(corpus, bookPlainText);
        var output = _writer.Write(working);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, output, cancellationToken).ConfigureAwait(false);
    }

    private static string JoinPlainText(IReadOnlyList<EpubChapter> chapters)
    {
        var builder = new StringBuilder();
        foreach (var chapter in chapters)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(chapter.PlainText);
        }

        return builder.ToString();
    }
}
