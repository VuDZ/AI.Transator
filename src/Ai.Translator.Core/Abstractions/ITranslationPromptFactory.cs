using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface ITranslationPromptFactory
{
    string Create(GlossaryDocument working, string styleRules);
}
