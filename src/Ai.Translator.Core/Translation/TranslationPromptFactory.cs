using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Translation;

public sealed class TranslationPromptFactory : ITranslationPromptFactory
{
    private readonly IGlossaryWriter _writer;

    public TranslationPromptFactory(IGlossaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    public string Create(GlossaryDocument working, string styleRules)
    {
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(styleRules);

        var glossaryMarkdown = _writer.Write(working);
        if (string.IsNullOrWhiteSpace(styleRules))
        {
            return glossaryMarkdown;
        }

        return styleRules.TrimEnd() + "\n\n" + glossaryMarkdown;
    }
}
