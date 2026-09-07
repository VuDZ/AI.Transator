using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryExtractor
{
    Task<GlossaryDocument> ExtractAsync(
        EpubBookModel original,
        EpubBookModel translation,
        GlossaryDocument? existingCorpus,
        string? model,
        IReadOnlyList<PairMapEntry>? pairMap,
        int? maxPairs,
        CancellationToken cancellationToken);
}
