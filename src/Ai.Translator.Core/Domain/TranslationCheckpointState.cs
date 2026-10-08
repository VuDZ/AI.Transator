namespace Ai.Translator.Core.Domain;

public sealed class TranslationCheckpointState
{
    public required string InputPath { get; set; }

    public required string OutputPath { get; set; }

    public required string Model { get; set; }

    public required string PrefixHash { get; set; }

    public string? ChunkPlanHash { get; set; }

    public int ChapterFrom { get; set; }

    public int ChapterTo { get; set; }

    public List<ChunkCheckpoint> Chunks { get; set; } = [];
}
