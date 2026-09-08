namespace Ai.Translator.Core.Domain;

public sealed class ExtractCheckpointState
{
    public required string OriginalPath { get; set; }

    public required string TranslationPath { get; set; }

    public string? PairsHash { get; set; }

    public int? MaxPairs { get; set; }

    public string? MergeIntoPath { get; set; }

    public required string Model { get; set; }

    public int ContextWindowTokens { get; set; }

    public int ReservedOutputTokens { get; set; }

    public List<string> CompletedSteps { get; set; } = [];
}
