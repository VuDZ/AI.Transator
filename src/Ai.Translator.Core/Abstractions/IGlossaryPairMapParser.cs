using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryPairMapParser
{
    IReadOnlyList<PairMapEntry> Parse(string text);
}
