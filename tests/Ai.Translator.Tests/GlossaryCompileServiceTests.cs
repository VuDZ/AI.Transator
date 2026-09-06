using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;
using Moq;

namespace Ai.Translator.Tests;

public sealed class GlossaryCompileServiceTests
{
    [Fact]
    public async Task CompileAsync_WritesWorkingGlossaryFromBookText()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var corpusPath = Path.Combine(root, "corpus.md");
            var bookPath = Path.Combine(root, "book.epub");
            var outPath = Path.Combine(root, "working.md");
            await File.WriteAllTextAsync(
                corpusPath,
                """
                # Warhammer 40k

                Preamble stays.

                ## Adeptus Astartes
                - ru: Адептус Астартес
                - type: organization
                - aliases: Astartes

                ## Ciaphas Cain
                - ru: Кайафас Каин
                - type: name
                """);
            await File.WriteAllTextAsync(bookPath, "placeholder");

            var epub = CreateEpubMock(bookPath, "The anti-Astartes relic");
            var service = new GlossaryCompileService(
                new GlossaryParser(),
                new GlossaryWriter(),
                new GlossaryCompiler(),
                epub.Object);

            await service.CompileAsync(corpusPath, bookPath, outPath, CancellationToken.None);

            var written = await File.ReadAllTextAsync(outPath);
            Assert.Contains("Preamble stays.", written, StringComparison.Ordinal);
            Assert.Contains("## Adeptus Astartes", written, StringComparison.Ordinal);
            Assert.DoesNotContain("Ciaphas Cain", written, StringComparison.Ordinal);
            epub.VerifyAll();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompileAsync_EmptyCorpus_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var corpusPath = Path.Combine(root, "corpus.md");
            var bookPath = Path.Combine(root, "book.epub");
            await File.WriteAllTextAsync(corpusPath, "# Title\n\nOnly preamble.\n");
            await File.WriteAllTextAsync(bookPath, "placeholder");

            var epub = new Mock<IEpubBookService>(MockBehavior.Strict);
            var service = new GlossaryCompileService(
                new GlossaryParser(),
                new GlossaryWriter(),
                new GlossaryCompiler(),
                epub.Object);

            var ex = await Assert.ThrowsAsync<GlossaryFormatException>(() =>
                service.CompileAsync(corpusPath, bookPath, Path.Combine(root, "out.md"), CancellationToken.None));
            Assert.Contains("## headings", ex.Message, StringComparison.Ordinal);
            epub.Verify(x => x.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompileAsync_NoMatches_WritesParseableWorkingGlossary()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var corpusPath = Path.Combine(root, "corpus.md");
            var bookPath = Path.Combine(root, "book.epub");
            var outPath = Path.Combine(root, "working.md");
            await File.WriteAllTextAsync(
                corpusPath,
                """
                # Warhammer 40k

                Preamble stays.

                ## Ciaphas Cain
                - ru: Кайафас Каин
                - type: name
                """);
            await File.WriteAllTextAsync(bookPath, "placeholder");

            var epub = CreateEpubMock(bookPath, "No glossary terms in this book.");
            var service = new GlossaryCompileService(
                new GlossaryParser(),
                new GlossaryWriter(),
                new GlossaryCompiler(),
                epub.Object);

            await service.CompileAsync(corpusPath, bookPath, outPath, CancellationToken.None);

            var written = await File.ReadAllTextAsync(outPath);
            var parsed = new GlossaryParser().Parse(written);
            Assert.Equal("Warhammer 40k", parsed.Title);
            Assert.Equal("Preamble stays.", parsed.Preamble);
            Assert.Empty(parsed.Entries);
            Assert.DoesNotContain("Ciaphas Cain", written, StringComparison.Ordinal);
            epub.VerifyAll();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompileAsync_MissingCorpus_Throws()
    {
        var epub = new Mock<IEpubBookService>(MockBehavior.Strict);
        var service = new GlossaryCompileService(
            new GlossaryParser(),
            new GlossaryWriter(),
            new GlossaryCompiler(),
            epub.Object);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.CompileAsync("missing.md", "book.epub", "out.md", CancellationToken.None));
    }

    [Fact]
    public async Task CompileAsync_RejectsNonEpubBook()
    {
        var epub = new Mock<IEpubBookService>(MockBehavior.Strict);
        var service = new GlossaryCompileService(
            new GlossaryParser(),
            new GlossaryWriter(),
            new GlossaryCompiler(),
            epub.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompileAsync("corpus.md", "book.txt", "out.md", CancellationToken.None));
        Assert.Contains("EPUB", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Mock<IEpubBookService> CreateEpubMock(string bookPath, string plainText)
    {
        var epub = new Mock<IEpubBookService>(MockBehavior.Strict);
        epub
            .Setup(x => x.OpenAsync(bookPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EpubBookModel
            {
                Chapters =
                [
                    new EpubChapter
                    {
                        FilePath = "OEBPS/chapter1.xhtml",
                        Xhtml = "<html><body><p>" + plainText + "</p></body></html>",
                        PlainText = plainText,
                        BodyInnerHtml = "<p>" + plainText + "</p>"
                    }
                ]
            });
        return epub;
    }
}
