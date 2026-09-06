using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryMerger : IGlossaryMerger
{
    public GlossaryDocument Merge(GlossaryDocument existing, GlossaryDocument incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(existing.Title);
        ArgumentNullException.ThrowIfNull(existing.Entries);
        ArgumentNullException.ThrowIfNull(incoming.Entries);

        var entries = new List<GlossaryEntry>(existing.Entries.Count + incoming.Entries.Count);
        var indexByEnglish = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in existing.Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.English);
            indexByEnglish[entry.English] = entries.Count;
            entries.Add(entry);
        }

        foreach (var incomingEntry in incoming.Entries)
        {
            ArgumentNullException.ThrowIfNull(incomingEntry);
            ArgumentNullException.ThrowIfNull(incomingEntry.English);
            if (indexByEnglish.TryGetValue(incomingEntry.English, out var index))
            {
                entries[index] = MergeEntry(entries[index], incomingEntry);
                continue;
            }

            indexByEnglish[incomingEntry.English] = entries.Count;
            entries.Add(incomingEntry);
        }

        return new GlossaryDocument
        {
            Title = existing.Title,
            Preamble = existing.Preamble ?? string.Empty,
            Entries = entries
        };
    }

    private static GlossaryEntry MergeEntry(GlossaryEntry existing, GlossaryEntry incoming)
    {
        return new GlossaryEntry
        {
            English = existing.English,
            Ru = existing.Ru,
            Kind = existing.Kind,
            Notes = MergeNotes(existing.Notes, incoming.Notes),
            DoNotUse = UnionPreserve(existing.DoNotUse, incoming.DoNotUse),
            Aliases = UnionPreserve(existing.Aliases, incoming.Aliases)
        };
    }

    private static string? MergeNotes(string? existing, string? incoming)
    {
        var left = string.IsNullOrWhiteSpace(existing) ? null : existing.Trim();
        var right = string.IsNullOrWhiteSpace(incoming) ? null : incoming.Trim();
        if (right is null)
        {
            return left;
        }

        if (left is null)
        {
            return right;
        }

        if (left.Contains(right, StringComparison.OrdinalIgnoreCase))
        {
            return left;
        }

        return left + " " + right;
    }

    private static IReadOnlyList<string> UnionPreserve(
        IReadOnlyList<string> existing,
        IReadOnlyList<string> incoming)
    {
        var left = existing ?? [];
        var right = incoming ?? [];
        var result = new List<string>(left.Count + right.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendUnique(left, result, seen);
        AppendUnique(right, result, seen);
        return result;
    }

    private static void AppendUnique(
        IReadOnlyList<string> source,
        List<string> result,
        HashSet<string> seen)
    {
        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var trimmed = item.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }
    }
}
