using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;

namespace Ai.Translator.Tests;

public sealed class GlossaryCompilerTests
{
    private static readonly GlossaryDocument Corpus = new()
    {
        Title = "Warhammer 40k",
        Preamble = "Preamble: keep this.",
        Entries =
        [
            new GlossaryEntry
            {
                English = "Adeptus Astartes",
                Ru = "Адептус Астартес",
                Kind = GlossaryEntryKind.Organization,
                Aliases = ["Astartes"]
            },
            new GlossaryEntry
            {
                English = "Ciaphas Cain",
                Ru = "Кайафас Каин",
                Kind = GlossaryEntryKind.Name
            },
            new GlossaryEntry
            {
                English = "Chapter",
                Ru = "Орден",
                Kind = GlossaryEntryKind.Organization
            }
        ]
    };

    [Fact]
    public void Compile_KeepsAstartesOnHyphenatedForm_AndDropsMissingName()
    {
        var compiler = new GlossaryCompiler();
        var working = compiler.Compile(Corpus, "The anti-Astartes relic");

        Assert.Equal(Corpus.Title, working.Title);
        Assert.Equal(Corpus.Preamble, working.Preamble);
        var entry = Assert.Single(working.Entries);
        Assert.Equal("Adeptus Astartes", entry.English);
        Assert.Equal(["Astartes"], entry.Aliases);
    }

    [Fact]
    public void Compile_MatchesPossessiveAndTypographicApostrophe()
    {
        var compiler = new GlossaryCompiler();
        var ascii = compiler.Compile(Corpus, "The Astartes' relic");
        var typographic = compiler.Compile(Corpus, "The Astartes\u2019 relic");

        Assert.Equal("Adeptus Astartes", Assert.Single(ascii.Entries).English);
        Assert.Equal("Adeptus Astartes", Assert.Single(typographic.Entries).English);
    }

    [Fact]
    public void Compile_DoesNotMatchAsciiLetterOnTheSide()
    {
        var compiler = new GlossaryCompiler();
        var working = compiler.Compile(Corpus, "Chaptersomething arrived.");
        Assert.Empty(working.Entries);
    }

    [Fact]
    public void Compile_MatchesMultiWordCanonicalForm()
    {
        var compiler = new GlossaryCompiler();
        var working = compiler.Compile(Corpus, "The Adeptus Astartes stood watch.");
        Assert.Equal("Adeptus Astartes", Assert.Single(working.Entries).English);
    }

    [Fact]
    public void Compile_IsStableOnRepeatedRuns()
    {
        var compiler = new GlossaryCompiler();
        var writer = new GlossaryWriter();
        const string text = "The anti-Astartes relic";

        var first = writer.Write(compiler.Compile(Corpus, text));
        var second = writer.Write(compiler.Compile(Corpus, text));

        Assert.Equal(first, second);
    }
}
