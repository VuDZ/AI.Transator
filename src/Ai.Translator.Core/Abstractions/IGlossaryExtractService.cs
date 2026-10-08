namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryExtractService
{
    Task ExtractAsync(
        string originalPath,
        string translationPath,
        string outputPath,
        string? mergeIntoPath,
        string? model,
        string? pairsPath,
        int? maxPairs,
        CancellationToken cancellationToken,
        string? workDir = null,
        bool resume = false,
        int? concurrency = null);
}
