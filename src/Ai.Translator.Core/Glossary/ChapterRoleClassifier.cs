using System.Globalization;
using System.Text.RegularExpressions;

namespace Ai.Translator.Core.Glossary;

internal static partial class ChapterRoleClassifier
{
    private const int StartWindowChars = 400;

    private static readonly (string Word, int Number)[] ChapterWords = BuildChapterWords();

    public static string Classify(string? title, string? plainText)
    {
        var heading = title ?? string.Empty;
        var body = plainText ?? string.Empty;
        var start = body.Length <= StartWindowChars ? body : body[..StartWindowChars];
        var haystack = heading.Length == 0 ? start : heading + "\n" + start;

        if (Contains(haystack, "dramatis") || Contains(haystack, "действующ"))
        {
            return "dramatis";
        }

        if (Contains(haystack, "vessels") || Contains(haystack, "корабл") || Contains(haystack, "prominent vessels"))
        {
            return "vessels";
        }

        if (Contains(haystack, "41st millennium")
            || Contains(haystack, "десять тысяч лет")
            || Contains(haystack, "it is the 41st"))
        {
            return "legend";
        }

        if (Contains(haystack, "epilogue") || Contains(haystack, "эпилог"))
        {
            return "epilogue";
        }

        if (Contains(haystack, "appendix") || Contains(haystack, "приложен"))
        {
            return "appendix";
        }

        if (Contains(haystack, "about the author") || Contains(haystack, "об авторе"))
        {
            return "author";
        }

        var chapter = TryChapterNumber(haystack);
        return chapter is null ? "other" : "chapter " + chapter.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool Contains(string haystack, string needle)
    {
        return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryChapterNumber(string text)
    {
        foreach (Match match in ChapterMarkerRegex().Matches(text))
        {
            var rest = match.Groups[1].Value.Trim();
            if (rest.Length == 0)
            {
                continue;
            }

            var normalized = rest.Replace('-', ' ');
            var digit = Regex.Match(normalized, "^[0-9]{1,2}");
            if (digit.Success
                && int.TryParse(digit.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                && n is >= 1 and <= 40)
            {
                return n;
            }

            foreach (var (word, number) in ChapterWords)
            {
                if (StartsWithWord(normalized, word))
                {
                    return number;
                }
            }
        }

        return null;
    }

    private static bool StartsWithWord(string text, string word)
    {
        if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Length == word.Length || !char.IsLetter(text[word.Length]);
    }

    [GeneratedRegex(@"(?:chapter|глава)\s+(.{1,40})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterMarkerRegex();

    private static (string Word, int Number)[] BuildChapterWords()
    {
        var englishUnits = new[]
        {
            "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"
        };
        var englishTeens = new[]
        {
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
            "seventeen", "eighteen", "nineteen"
        };
        var russian = new (string Word, int Number)[]
        {
            ("первая", 1), ("вторая", 2), ("третья", 3),
            ("четвёртая", 4), ("четвертая", 4),
            ("пятая", 5), ("шестая", 6), ("седьмая", 7),
            ("восьмая", 8), ("девятая", 9), ("десятая", 10),
            ("одиннадцатая", 11), ("двенадцатая", 12),
            ("тринадцатая", 13), ("четырнадцатая", 14),
            ("пятнадцатая", 15), ("шестнадцатая", 16),
            ("семнадцатая", 17), ("восемнадцатая", 18),
            ("девятнадцатая", 19), ("двадцатая", 20),
            ("тридцатая", 30), ("сороковая", 40)
        };

        var list = new List<(string Word, int Number)>(80);
        for (var i = 0; i < englishUnits.Length; i++)
        {
            list.Add((englishUnits[i], i + 1));
        }

        for (var i = 0; i < englishTeens.Length; i++)
        {
            list.Add((englishTeens[i], i + 10));
        }

        list.Add(("twenty", 20));
        list.Add(("thirty", 30));
        list.Add(("forty", 40));
        for (var i = 0; i < englishUnits.Length; i++)
        {
            list.Add(("twenty " + englishUnits[i], 21 + i));
            list.Add(("thirty " + englishUnits[i], 31 + i));
        }

        foreach (var (word, number) in russian)
        {
            list.Add((word, number));
            if (number is >= 1 and <= 9)
            {
                list.Add(("двадцать " + word, 20 + number));
                list.Add(("тридцать " + word, 30 + number));
            }
        }

        list.Sort((left, right) => right.Word.Length.CompareTo(left.Word.Length));
        return [.. list];
    }
}
