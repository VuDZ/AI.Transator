using Ai.Translator.Core.Epub;

namespace Ai.Translator.Tests;

public sealed class EpubBookTextExtractorTests
{
    [Fact]
    public async Task ExtractPlainTextAsync_ReadsChapterBody()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var epubPath = MinimalEpubFactory.Create(root, "<p>The anti-Astartes relic</p><p>Second block</p>");
            var extractor = new EpubBookTextExtractor();

            var text = await extractor.ExtractPlainTextAsync(epubPath, CancellationToken.None);

            Assert.Contains("anti-Astartes", text, StringComparison.Ordinal);
            Assert.Contains("Second block", text, StringComparison.Ordinal);
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
    public async Task ExtractPlainTextAsync_CorruptFile_ThrowsReadableError()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "broken.epub");
            await File.WriteAllTextAsync(path, "not-an-epub");
            var extractor = new EpubBookTextExtractor();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => extractor.ExtractPlainTextAsync(path, CancellationToken.None));
            Assert.StartsWith("Failed to read EPUB:", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
