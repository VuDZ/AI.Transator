namespace Ai.Translator.Core.Domain;

public sealed class TranslationChunk
{
    public required string Id { get; init; }

    public required string ChapterFilePath { get; init; }

    public required int ChapterNumber { get; init; }

    public required string SourceHtml { get; init; }
}
