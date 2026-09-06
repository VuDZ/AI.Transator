using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryMerger
{
    GlossaryDocument Merge(GlossaryDocument existing, GlossaryDocument incoming);
}
