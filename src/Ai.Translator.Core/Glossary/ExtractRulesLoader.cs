namespace Ai.Translator.Core.Glossary;

public sealed class ExtractRulesLoader
{
    public const string RelativePath = "prompts/extract-system.md";

    private readonly string _baseDirectory;

    public ExtractRulesLoader()
        : this(AppContext.BaseDirectory)
    {
    }

    public ExtractRulesLoader(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        _baseDirectory = baseDirectory;
    }

    public async Task<string> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_baseDirectory, RelativePath);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
