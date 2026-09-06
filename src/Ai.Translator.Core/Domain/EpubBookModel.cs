namespace Ai.Translator.Core.Domain;

public sealed class EpubBookModel
{
    public IReadOnlyList<EpubChapter> Chapters { get; init; } = [];
}
