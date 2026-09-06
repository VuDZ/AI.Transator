using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;

namespace Ai.Translator.Tests;

public sealed class GlossaryRoundTripTests
{
    private const string CanonicalMarkdown =
        """
        # Warhammer 40k

        Preamble: нормы издания, ты/вы, что не переводим глобально.
        Пустые строки допустимы.

        ## Librarian
        - ru: Библиарий
        - type: title
        - notes: Не использовать «Библиотекарь».
        - do-not-use: Библиотекарь
        - aliases: Librarians, Librarian's

        ## Adeptus Astartes
        - ru: Адептус Астартес
        - type: organization
        - notes: не склоняется
        - aliases: Astartes
        """;

    [Fact]
    public void ParseWriteParse_PreservesKnownFields()
    {
        var parser = new GlossaryParser();
        var writer = new GlossaryWriter();

        var first = parser.Parse(CanonicalMarkdown);
        var written = writer.Write(first);
        var second = parser.Parse(written);

        AssertDocumentsEqual(first, second);
        Assert.Equal("Библиарий", second.Entries[0].Ru);
        Assert.Equal(GlossaryEntryKind.Title, second.Entries[0].Kind);
        Assert.Equal("Не использовать «Библиотекарь».", second.Entries[0].Notes);
        Assert.Equal(["Библиотекарь"], second.Entries[0].DoNotUse);
        Assert.Equal(["Librarians", "Librarian's"], second.Entries[0].Aliases);
    }

    [Fact]
    public void Parse_IgnoresUnknownKeys_WithoutLosingKnownFields()
    {
        const string markdown =
            """
            # Corpus

            ## Librarian
            - ru: Библиарий
            - type: title
            - extra: should-be-ignored
            - notes: keep me
            """;

        var document = new GlossaryParser().Parse(markdown);
        var entry = Assert.Single(document.Entries);
        Assert.Equal("Библиарий", entry.Ru);
        Assert.Equal(GlossaryEntryKind.Title, entry.Kind);
        Assert.Equal("keep me", entry.Notes);
    }

    [Fact]
    public void Parse_EmptyCorpus_Throws()
    {
        var parser = new GlossaryParser();
        var ex = Assert.Throws<GlossaryFormatException>(() => parser.Parse("   \n"));
        Assert.Equal("Corpus is empty.", ex.Message);
    }

    [Fact]
    public void ParseWriteParse_EmptyWorkingGlossary_PreservesTitleAndPreamble()
    {
        const string markdown =
            """
            # Warhammer 40k

            Preamble: keep this.
            """;

        var parser = new GlossaryParser();
        var writer = new GlossaryWriter();
        var first = parser.Parse(markdown);
        Assert.Empty(first.Entries);
        Assert.Equal("Warhammer 40k", first.Title);
        Assert.Equal("Preamble: keep this.", first.Preamble);

        var second = parser.Parse(writer.Write(first));
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Preamble, second.Preamble);
        Assert.Empty(second.Entries);
    }

    [Fact]
    public void Parse_MissingTitle_Throws()
    {
        var parser = new GlossaryParser();
        var ex = Assert.Throws<GlossaryFormatException>(() => parser.Parse("Only preamble.\n"));
        Assert.Contains("# heading", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseModelOutput_TakesEntriesFromFirstHeading_IgnoresSurroundingText()
    {
        const string response =
            """
            Sure, here are the terms I found.

            ## Librarian
            - ru: Библиарий
            - type: title

            ## Adeptus Astartes
            - ru: Адептус Астартес
            - type: organization

            Hope this helps.
            """;

        var parsed = new GlossaryParser().ParseModelOutput(response);
        Assert.Equal("Extracted", parsed.Title);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal("Librarian", parsed.Entries[0].English);
        Assert.Equal("Библиарий", parsed.Entries[0].Ru);
        Assert.Equal("Adeptus Astartes", parsed.Entries[1].English);
    }

    [Fact]
    public void ParseModelOutput_NoEntries_ReturnsEmptyDocument()
    {
        var parsed = new GlossaryParser().ParseModelOutput("No glossary terms in this chapter.");
        Assert.Equal("Extracted", parsed.Title);
        Assert.Empty(parsed.Entries);
    }

    private static void AssertDocumentsEqual(GlossaryDocument expected, GlossaryDocument actual)
    {
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Preamble, actual.Preamble);
        Assert.Equal(expected.Entries.Count, actual.Entries.Count);
        for (var i = 0; i < expected.Entries.Count; i++)
        {
            var left = expected.Entries[i];
            var right = actual.Entries[i];
            Assert.Equal(left.English, right.English);
            Assert.Equal(left.Ru, right.Ru);
            Assert.Equal(left.Kind, right.Kind);
            Assert.Equal(left.Notes, right.Notes);
            Assert.Equal(left.DoNotUse, right.DoNotUse);
            Assert.Equal(left.Aliases, right.Aliases);
        }
    }
}
