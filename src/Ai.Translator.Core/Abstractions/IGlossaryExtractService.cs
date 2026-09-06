namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryExtractService
{
    Task ExtractAsync(
        string originalPath,
        string translationPath,
        string outputPath,
        string? mergeIntoPath,
        string? model,
        CancellationToken cancellationToken);
}
