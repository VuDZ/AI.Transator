using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Translation;

namespace Ai.Translator.Tests;

public sealed class TranslationPromptFactoryTests
{
    [Fact]
    public void Create_IncludesGlossaryAndStyle_NotChapterText()
    {
        var working = new GlossaryDocument
        {
            Title = "Warhammer 40k",
            Preamble = "Use ты.",
            Entries =
            [
                new GlossaryEntry
                {
                    English = "Librarian",
                    Ru = "Библиарий",
                    Kind = GlossaryEntryKind.Title
                }
            ]
        };
        var factory = new TranslationPromptFactory(new GlossaryWriter());

        var prefix = factory.Create(working, "Translate to Russian. Keep HTML.");

        Assert.Contains("Translate to Russian. Keep HTML.", prefix, StringComparison.Ordinal);
        Assert.Contains("Use ты.", prefix, StringComparison.Ordinal);
        Assert.Contains("## Librarian", prefix, StringComparison.Ordinal);
        Assert.Contains("Библиарий", prefix, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE_CHAPTER_PHRASE", prefix, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", prefix, StringComparison.Ordinal);
    }
}
