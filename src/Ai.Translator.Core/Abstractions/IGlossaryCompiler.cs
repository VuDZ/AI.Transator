using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryCompiler
{
    GlossaryDocument Compile(GlossaryDocument corpus, string bookPlainText);
}
