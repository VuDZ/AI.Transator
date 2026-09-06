using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;

namespace Ai.Translator.Tests;

public sealed class GlossaryMergerTests
{
    [Fact]
    public void Merge_KeepsExistingRu_AppendsNewAlias_AddsNewEntryAtEnd()
    {
        var existing = new GlossaryDocument
        {
            Title = "Warhammer 40k",
            Preamble = "Keep preamble.",
            Entries =
            [
                new GlossaryEntry
                {
                    English = "Librarian",
                    Ru = "Библиарий",
                    Kind = GlossaryEntryKind.Title,
                    Notes = "Keep notes.",
                    Aliases = ["Librarians"]
                },
                new GlossaryEntry
                {
                    English = "bolter",
                    Ru = "болтер",
                    Kind = GlossaryEntryKind.Artifact
                }
            ]
        };

        var incoming = new GlossaryDocument
        {
            Title = "Extracted",
            Entries =
            [
                new GlossaryEntry
                {
                    English = "Librarian",
                    Ru = "Библиотекарь",
                    Kind = GlossaryEntryKind.Other,
                    Notes = "New note.",
                    Aliases = ["Librarian's"],
                    DoNotUse = ["библиотекарь"]
                },
                new GlossaryEntry
                {
                    English = "Adeptus Astartes",
                    Ru = "Адептус Астартес",
                    Kind = GlossaryEntryKind.Organization,
                    Aliases = ["Astartes"]
                }
            ]
        };

        var merged = new GlossaryMerger().Merge(existing, incoming);

        Assert.Equal("Warhammer 40k", merged.Title);
        Assert.Equal("Keep preamble.", merged.Preamble);
        Assert.Equal(3, merged.Entries.Count);

        var librarian = merged.Entries[0];
        Assert.Equal("Librarian", librarian.English);
        Assert.Equal("Библиарий", librarian.Ru);
        Assert.Equal(GlossaryEntryKind.Title, librarian.Kind);
        Assert.Equal("Keep notes. New note.", librarian.Notes);
        Assert.Equal(["Librarians", "Librarian's"], librarian.Aliases);
        Assert.Equal(["библиотекарь"], librarian.DoNotUse);

        Assert.Equal("bolter", merged.Entries[1].English);
        Assert.Equal("болтер", merged.Entries[1].Ru);

        var added = merged.Entries[2];
        Assert.Equal("Adeptus Astartes", added.English);
        Assert.Equal("Адептус Астартес", added.Ru);
        Assert.Equal(["Astartes"], added.Aliases);
    }

    [Fact]
    public void Merge_MatchesEnglishIgnoreCase_DoesNotOverwriteRu()
    {
        var existing = new GlossaryDocument
        {
            Title = "Corpus",
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

        var incoming = new GlossaryDocument
        {
            Title = "Extracted",
            Entries =
            [
                new GlossaryEntry
                {
                    English = "librarian",
                    Ru = "Библиотекарь",
                    Kind = GlossaryEntryKind.Name,
                    Aliases = ["Librarians"]
                }
            ]
        };

        var merged = new GlossaryMerger().Merge(existing, incoming);
        var entry = Assert.Single(merged.Entries);
        Assert.Equal("Librarian", entry.English);
        Assert.Equal("Библиарий", entry.Ru);
        Assert.Equal(GlossaryEntryKind.Title, entry.Kind);
        Assert.Equal(["Librarians"], entry.Aliases);
    }

    [Fact]
    public void Merge_DoesNotDuplicateExistingAlias()
    {
        var existing = new GlossaryDocument
        {
            Title = "Corpus",
            Entries =
            [
                new GlossaryEntry
                {
                    English = "Librarian",
                    Ru = "Библиарий",
                    Aliases = ["Librarians"]
                }
            ]
        };

        var incoming = new GlossaryDocument
        {
            Title = "Extracted",
            Entries =
            [
                new GlossaryEntry
                {
                    English = "Librarian",
                    Ru = "Библиотекарь",
                    Aliases = ["librarians", "Librarian's"]
                }
            ]
        };

        var merged = new GlossaryMerger().Merge(existing, incoming);
        Assert.Equal(["Librarians", "Librarian's"], Assert.Single(merged.Entries).Aliases);
    }
}
