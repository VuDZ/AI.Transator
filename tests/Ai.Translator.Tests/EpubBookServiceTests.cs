using System.IO.Compression;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Epub;

namespace Ai.Translator.Tests;

public sealed class EpubBookServiceTests
{
    [Fact]
    public async Task OpenAsync_ReadsSpineChapterWithInlineMarkupAndBlocks()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var epubPath = MinimalEpubFactory.Create(
                root,
                "<p>The anti-Astartes <em>relic</em> and <i>blade</i></p><p>Second block</p>");
            var service = new EpubBookService();

            var book = await service.OpenAsync(epubPath, CancellationToken.None);

            var chapter = Assert.Single(book.Chapters);
            Assert.Equal("OEBPS/chapter1.xhtml", chapter.FilePath);
            Assert.Contains("anti-Astartes", chapter.PlainText, StringComparison.Ordinal);
            Assert.Contains("Second block", chapter.PlainText, StringComparison.Ordinal);
            Assert.Contains("<em>", chapter.BodyInnerHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<i>", chapter.BodyInnerHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, chapter.BlockFragments.Count);
            Assert.Contains("<em>", chapter.BlockFragments[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<i>", chapter.BlockFragments[0], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<em>", chapter.PlainText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenAsync_SkipsEmptySpineItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var epubPath = MinimalEpubFactory.Create(
                root,
                "   ",
                "<p>Only this chapter has text</p>");
            var service = new EpubBookService();

            var book = await service.OpenAsync(epubPath, CancellationToken.None);

            var chapter = Assert.Single(book.Chapters);
            Assert.Equal("OEBPS/chapter2.xhtml", chapter.FilePath);
            Assert.Contains("Only this chapter has text", chapter.PlainText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteCopyAsync_ReplacesBodyKeepsAssetsAndSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var sourcePath = MinimalEpubFactory.Create(root, "<p>Original chapter</p>");
            var originalBytes = await File.ReadAllBytesAsync(sourcePath);
            var destinationPath = Path.Combine(root, "copy.epub");
            var service = new EpubBookService();

            await service.WriteCopyAsync(
                sourcePath,
                destinationPath,
                [
                    new EpubReplace
                    {
                        FilePath = "OEBPS\\chapter1.xhtml",
                        BodyInnerHtml = "<p>Translated chapter</p>"
                    }
                ],
                CancellationToken.None);

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sourcePath));

            using (var zip = ZipFile.OpenRead(destinationPath))
            {
                Assert.Equal("mimetype", zip.Entries[0].FullName);
                Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);
                Assert.NotNull(zip.GetEntry("OEBPS/styles/style.css"));
                Assert.NotNull(zip.GetEntry("OEBPS/images/pixel.png"));
            }

            var copy = await service.OpenAsync(destinationPath, CancellationToken.None);
            var chapter = Assert.Single(copy.Chapters);
            Assert.Contains("Translated chapter", chapter.PlainText, StringComparison.Ordinal);
            Assert.DoesNotContain("Original chapter", chapter.PlainText, StringComparison.Ordinal);
            Assert.Contains("styles/style.css", chapter.Xhtml, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenAsync_CorruptFile_ThrowsReadableError()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "broken.epub");
            await File.WriteAllTextAsync(path, "not-an-epub");
            var service = new EpubBookService();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.OpenAsync(path, CancellationToken.None));
            Assert.StartsWith("Failed to read EPUB:", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
