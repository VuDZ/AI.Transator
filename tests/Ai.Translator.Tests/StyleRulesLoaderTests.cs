using Ai.Translator.Core.Translation;

namespace Ai.Translator.Tests;

public sealed class StyleRulesLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReadsPromptFileInsteadOfFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "prompts"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "prompts", "translate-system.md"),
                "FROM FILE\n");
            var loader = new StyleRulesLoader(root);

            var loaded = await loader.LoadAsync("FROM OPTIONS", CancellationToken.None);

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
    public async Task LoadAsync_MissingFile_UsesFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var loader = new StyleRulesLoader(root);

            var loaded = await loader.LoadAsync("FROM OPTIONS", CancellationToken.None);

            Assert.Equal("FROM OPTIONS", loaded);
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
