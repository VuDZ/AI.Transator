using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Translation;

public sealed class TranslationValidator : ITranslationValidator
{
    public const string EnglishFunctionWordReasonPrefix = "English function word: ";

    private static readonly HashSet<string> EnglishFunctionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "was", "with", "that",
        "a", "an", "of", "to", "in", "for", "on", "at", "by",
        "is", "are", "were", "be", "been", "this", "these", "those", "from", "or", "but", "not"
    };

    private static readonly Regex TagNameRegex = new(
        @"</?([A-Za-z][A-Za-z0-9]*)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BlockOpenRegex = new(
        @"<(p|h1|h2|h3|h4|h5|h6|blockquote|li)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

private static readonly Regex WordRegex = new(
        @"[\p{L}\p{N}_]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BinaryRunRegex = new(
        @"[01]{8,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ValidationResult Validate(string sourceChunk, LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(sourceChunk);
        ArgumentNullException.ThrowIfNull(response);

        var content = response.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return ValidationResult.Fail("Empty model response.");
        }

        if (IsTruncated(response.FinishReason))
        {
            return ValidationResult.Fail($"Response was truncated (finish_reason={response.FinishReason}).");
        }

        if (HasNoSourceTags(sourceChunk, content))
        {
            return ValidationResult.Fail("Response looks like reasoning instead of HTML (no source tags preserved).");
        }

        if (HasDroppedBlocks(sourceChunk, content))
        {
            return ValidationResult.Fail("Translated HTML dropped too many block elements.");
        }

        return ValidateEnglish(content);
    }

    private static bool IsTruncated(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason))
        {
            return false;
        }

        return finishReason.Equals("length", StringComparison.OrdinalIgnoreCase)
            || finishReason.Equals("max_tokens", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNoSourceTags(string sourceChunk, string content)
    {
        var sourceTags = DistinctTagNames(sourceChunk);
        if (sourceTags.Count == 0)
        {
            return false;
        }

        var contentTags = DistinctTagNames(content);
        foreach (var tag in sourceTags)
        {
            if (contentTags.Contains(tag))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> DistinctTagNames(string html)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TagNameRegex.Matches(html))
        {
            var name = match.Groups[1].Value;
            if (name.Equals("html", StringComparison.OrdinalIgnoreCase)
                || name.Equals("head", StringComparison.OrdinalIgnoreCase)
                || name.Equals("body", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            names.Add(name);
        }

        return names;
    }

    private static bool HasDroppedBlocks(string sourceChunk, string content)
    {
        var sourceBlocks = BlockOpenRegex.Matches(sourceChunk).Count;
        if (sourceBlocks < 2)
        {
            return false;
        }

        var translatedBlocks = BlockOpenRegex.Matches(content).Count;
        return translatedBlocks * 2 < sourceBlocks;
    }

    private static ValidationResult ValidateEnglish(string content)
    {
        var warnings = new List<string>();
        var plain = VisibleText(content);
        foreach (var line in plain.Split('\n'))
        {
            if (IsProtocolLike(line))
            {
                continue;
            }

            var run = new List<Match>();
            string? failure = null;

            void CheckRun()
            {
                if (run.Count == 0)
                {
                    return;
                }

                var functionWords = run.Where(word => EnglishFunctionWords.Contains(word.Value)).ToList();
                if (functionWords.Count > 0)
                {
                    var token = functionWords[0].Value;
                    var contextStart = Math.Max(0, run[0].Index - 30);
                    var snippet = line.Substring(contextStart, Math.Min(120, line.Length - contextStart));
                    snippet = Regex.Replace(snippet, @"\s+", " ").Replace('"', '\'');
                    var reason = EnglishFunctionWordReasonPrefix + token + "; context: \"" + snippet + "\"";
                    if (run.Count >= 3 || functionWords.Count >= 2)
                    {
                        failure = reason;
                    }
                    else if (warnings.Count < 3)
                    {
                        warnings.Add(reason);
                    }
                }

                run.Clear();
            }

            foreach (Match word in WordRegex.Matches(line))
            {
                if (run.Count > 0)
                {
                    var previous = run[^1];
                    var gap = line.AsSpan(previous.Index + previous.Length, word.Index - previous.Index - previous.Length);
                    if (!IsPhraseGap(gap))
                    {
                        CheckRun();
                    }
                }

                if (failure is not null)
                {
                    return ValidationResult.Fail(failure);
                }

                if (word.Value.All(char.IsAsciiLetter))
                {
                    run.Add(word);
                }
                else
                {
                    CheckRun();
                    if (failure is not null)
                    {
                        return ValidationResult.Fail(failure);
                    }
                }
            }

            CheckRun();
            if (failure is not null)
            {
                return ValidationResult.Fail(failure);
            }
        }

        return ValidationResult.Ok(warnings);
    }

    private static bool IsPhraseGap(ReadOnlySpan<char> gap)
    {
        foreach (var ch in gap)
        {
            if (!char.IsWhiteSpace(ch) && ch is not (',' or '\'' or '"' or '’' or '‘' or '«' or '»' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string VisibleText(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var text = new StringBuilder();

        void Visit(HtmlNode node)
        {
            if (node.NodeType == HtmlNodeType.Comment || node.Name is "head" or "script" or "style")
            {
                return;
            }

            if (node.NodeType == HtmlNodeType.Text)
            {
                text.Append(HtmlEntity.DeEntitize(node.InnerText));
                return;
            }

            var block = node.Name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                or "blockquote" or "li" or "div" or "section" or "article" or "br" or "tr" or "td" or "th" or "pre";
            if (block)
            {
                text.Append('\n');
            }

            foreach (var child in node.ChildNodes)
            {
                Visit(child);
            }

            if (block)
            {
                text.Append('\n');
            }
        }

        Visit(document.DocumentNode);
        return text.ToString();
    }

    private static bool IsProtocolLike(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (BinaryRunRegex.IsMatch(trimmed))
        {
            return true;
        }

        var digits = 0;
        var letters = 0;
        foreach (var ch in trimmed)
        {
            if (char.IsAsciiDigit(ch))
            {
                digits++;
            }
            else if (char.IsAsciiLetter(ch))
            {
                letters++;
            }
        }

        if (digits >= 6 && digits * 2 >= letters)
        {
            return true;
        }

        return digits >= 4 && trimmed.IndexOfAny(['=', '%', '/', '>']) >= 0;
    }
}
