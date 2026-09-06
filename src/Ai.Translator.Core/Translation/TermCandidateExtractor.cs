using System.Text;
using System.Text.RegularExpressions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;

namespace Ai.Translator.Core.Translation;

public sealed class TermCandidateExtractor
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "The", "A", "An", "And", "Of", "In", "On", "For", "To", "With", "That",
        "This", "These", "Those", "Was", "Were", "Is", "Are", "From", "By", "At"
    };

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CapitalizedPhraseRegex = new(
        @"(?<![A-Za-z])[A-Z][a-zA-Z]+(?:[ \t]+[A-Z][a-zA-Z]+)*(?![A-Za-z])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public IReadOnlyList<string> Extract(string sourceHtml, GlossaryDocument working)
    {
        ArgumentNullException.ThrowIfNull(sourceHtml);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(working.Entries);

        var plain = HtmlTagRegex.Replace(sourceHtml, " ");
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in CapitalizedPhraseRegex.Matches(plain))
        {
            var phrase = Regex.Replace(match.Value, @"\s+", " ").Trim();
            if (phrase.Length == 0 || seen.Contains(phrase))
            {
                continue;
            }

            if (IsStopPhrase(phrase) || IsInGlossary(phrase, working))
            {
                continue;
            }

            seen.Add(phrase);
            found.Add(phrase);
        }

        return found;
    }

    public static string ToMarkdown(IReadOnlyCollection<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var builder = new StringBuilder();
        builder.Append("# Term candidates\n");
        if (terms.Count == 0)
        {
            builder.Append('\n');
            return builder.ToString();
        }

        builder.Append('\n');
        foreach (var term in terms)
        {
            builder.Append("- ").Append(term).Append('\n');
        }

        return builder.ToString();
    }

    private static bool IsStopPhrase(string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return true;
        }

        foreach (var word in words)
        {
            if (!Stopwords.Contains(word))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInGlossary(string phrase, GlossaryDocument working)
    {
        foreach (var entry in working.Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (GlossaryTermMatcher.AppearsIn(phrase, entry.English)
                && phrase.Equals(entry.English, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(entry.English, phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var alias in entry.Aliases)
            {
                if (string.Equals(alias, phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
