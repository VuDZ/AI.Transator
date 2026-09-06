using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IChapterChunker
{
    IReadOnlyList<TranslationChunk> Chunk(
        EpubChapter chapter,
        int prefixTokenCount,
        ModelProfile modelProfile);
}
