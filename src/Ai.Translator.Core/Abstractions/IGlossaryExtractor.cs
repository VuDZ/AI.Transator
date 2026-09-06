using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryExtractor
{
    Task<GlossaryDocument> ExtractAsync(
        EpubBookModel original,
        EpubBookModel translation,
        GlossaryDocument? existingCorpus,
        string? model,
        CancellationToken cancellationToken);
}
