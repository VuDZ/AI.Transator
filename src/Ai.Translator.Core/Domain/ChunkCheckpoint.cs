namespace Ai.Translator.Core.Domain;

public sealed class ChunkCheckpoint
{
    public required string Id { get; set; }

    public required string ChapterFilePath { get; set; }

    public int ChapterNumber { get; set; }

    public required string Status { get; set; }
}
