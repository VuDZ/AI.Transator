using Ai.Translator.Core.Glossary;

namespace Ai.Translator.Tests;

public sealed class ExtractRulesLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReadsPromptFileInsteadOfEmptyFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "prompts"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "prompts", "extract-system.md"),
                "FROM FILE\n");
            var loader = new ExtractRulesLoader(root);

            var loaded = await loader.LoadAsync(CancellationToken.None);

            Assert.Equal("FROM FILE\n", loaded);
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
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var loader = new ExtractRulesLoader(root);

            var loaded = await loader.LoadAsync(CancellationToken.None);

            Assert.Equal(string.Empty, loaded);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
