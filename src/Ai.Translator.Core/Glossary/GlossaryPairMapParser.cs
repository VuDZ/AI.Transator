using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryPairMapParser : IGlossaryPairMapParser
{
    public IReadOnlyList<PairMapEntry> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var entries = new List<PairMapEntry>();
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var stripped = StripComment(raw).Trim();
            if (stripped.Length == 0)
            {
                continue;
            }

            var separator = stripped.IndexOf('=');
            if (separator < 0)
            {
                throw new InvalidOperationException(
                    $"Invalid pair mapping line: '{stripped}'. Expected 'left = right' or 'A .. B = C .. D'.");
            }

            if (stripped.IndexOf('=', separator + 1) >= 0)
            {
                throw new InvalidOperationException(
                    $"Invalid pair mapping line: '{stripped}'. Expected a single '=' between original and translation.");
            }

            var left = stripped[..separator].Trim();
            var right = stripped[(separator + 1)..].Trim();
            if (left.Length == 0 || right.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Invalid pair mapping line: '{stripped}'. Both sides of '=' are required.");
            }

            var original = SplitSide(left, stripped);
            var translation = SplitSide(right, stripped);
            if (original.IsRange != translation.IsRange)
            {
                throw new InvalidOperationException(
                    $"Invalid pair mapping line: '{stripped}'. Both sides must be a single path or a range.");
            }

            entries.Add(new PairMapEntry
            {
                OriginalFrom = original.From,
                OriginalTo = original.To,
                TranslationFrom = translation.From,
                TranslationTo = translation.To
            });
        }

        return entries;
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }

    private static (string From, string To, bool IsRange) SplitSide(string side, string line)
    {
        var rangeAt = IndexOfRangeSeparator(side);
        if (rangeAt < 0)
        {
            return (side, side, false);
        }

        if (IndexOfRangeSeparator(side, rangeAt + 2) >= 0)
        {
            throw new InvalidOperationException(
                $"Invalid pair mapping line: '{line}'. A range may contain only one '..'.");
        }

        var from = side[..rangeAt].Trim();
        var to = side[(rangeAt + 2)..].Trim();
        if (from.Length == 0 || to.Length == 0)
        {
            throw new InvalidOperationException(
                $"Invalid pair mapping line: '{line}'. Range bounds must be non-empty paths.");
        }

        return (from, to, true);
    }

    private static int IndexOfRangeSeparator(string text, int startIndex = 0)
    {
        return text.IndexOf("..", startIndex, StringComparison.Ordinal);
    }
}
