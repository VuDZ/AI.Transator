using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryParser
{
    GlossaryDocument Parse(string markdown);
}
