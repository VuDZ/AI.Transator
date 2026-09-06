namespace Ai.Translator.Core.Translation;

public sealed class StyleRulesLoader
{
    public const string RelativePath = "prompts/translate-system.md";

    private readonly string _baseDirectory;

    public StyleRulesLoader()
        : this(AppContext.BaseDirectory)
    {
    }

    public StyleRulesLoader(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        _baseDirectory = baseDirectory;
    }

    public async Task<string> LoadAsync(string? fallback, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_baseDirectory, RelativePath);
        if (!File.Exists(path))
        {
            return fallback ?? string.Empty;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
