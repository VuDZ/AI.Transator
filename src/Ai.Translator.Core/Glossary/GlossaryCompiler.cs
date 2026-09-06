using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryCompiler : IGlossaryCompiler
{
    public GlossaryDocument Compile(GlossaryDocument corpus, string bookPlainText)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(bookPlainText);
        ArgumentNullException.ThrowIfNull(corpus.Entries);

        var matched = new List<GlossaryEntry>();
        foreach (var entry in corpus.Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (IsPresent(bookPlainText, entry))
            {
                matched.Add(entry);
            }
        }

        return new GlossaryDocument
        {
            Title = corpus.Title,
            Preamble = corpus.Preamble,
            Entries = matched
        };
    }

    private static bool IsPresent(string bookPlainText, GlossaryEntry entry)
    {
        if (GlossaryTermMatcher.AppearsIn(bookPlainText, entry.English))
        {
            return true;
        }

        foreach (var alias in entry.Aliases)
        {
            if (GlossaryTermMatcher.AppearsIn(bookPlainText, alias))
            {
                return true;
            }
        }

        return false;
    }
}
