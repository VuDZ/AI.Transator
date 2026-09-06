namespace Ai.Translator.Core.Abstractions;

public interface IBookTextExtractor
{
    Task<string> ExtractPlainTextAsync(string epubPath, CancellationToken cancellationToken);
}
