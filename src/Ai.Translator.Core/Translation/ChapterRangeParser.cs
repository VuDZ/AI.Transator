using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Translation;

public static class ChapterRangeParser
{
    public static ChapterRange Parse(string? spec, int chapterCount)
    {
        if (chapterCount < 1)
        {
            throw new InvalidOperationException("The book has no chapters to translate.");
        }

        if (string.IsNullOrWhiteSpace(spec))
        {
            return new ChapterRange(1, chapterCount);
        }

        var text = spec.Trim();
        var dash = text.IndexOf('-');
        int from;
        int to;
        if (dash < 0)
        {
            if (!int.TryParse(text, out from))
            {
                throw new InvalidOperationException(
                    $"Invalid --chapters value '{spec}'. Use a 1-based index or an inclusive range (e.g. 3 or 2-4).");
            }

            to = from;
        }
        else
        {
            var left = text[..dash].Trim();
            var right = text[(dash + 1)..].Trim();
            if (!int.TryParse(left, out from) || !int.TryParse(right, out to))
            {
                throw new InvalidOperationException(
                    $"Invalid --chapters value '{spec}'. Use a 1-based index or an inclusive range (e.g. 3 or 2-4).");
            }
        }

        if (from > to)
        {
            throw new InvalidOperationException(
                $"Invalid --chapters range: from ({from}) is greater than to ({to}). The book has {chapterCount} chapters.");
        }

        if (from < 1 || to > chapterCount)
        {
            throw new InvalidOperationException(
                $"Chapter range is outside the book. The book has {chapterCount} chapters.");
        }

        return new ChapterRange(from, to);
    }
}
