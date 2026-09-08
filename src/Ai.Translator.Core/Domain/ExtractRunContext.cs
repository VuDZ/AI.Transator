namespace Ai.Translator.Core.Domain;

public sealed class ExtractRunContext
{
    public required string OutputPath { get; init; }

    public required string WorkDir { get; init; }

    public bool Resume { get; init; }

    public required string OriginalPath { get; init; }

    public required string TranslationPath { get; init; }

    public string? PairsHash { get; init; }

    public int? MaxPairs { get; init; }

    public string? MergeIntoPath { get; init; }
}
