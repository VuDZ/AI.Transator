using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryWriter
{
    string Write(GlossaryDocument document);
}
