using System.Text.RegularExpressions;
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

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LatinTokenRegex = new(
        @"[A-Za-z][A-Za-z0-9_]*",
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

        var leaked = FindEnglishFunctionWord(content);
        if (leaked is not null)
        {
            return ValidationResult.Fail(EnglishFunctionWordReasonPrefix + leaked);
        }

        return ValidationResult.Ok();
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

    private static string? FindEnglishFunctionWord(string content)
    {
        var plain = HtmlTagRegex.Replace(content, "\n");
        foreach (var line in plain.Split('\n'))
        {
            if (IsProtocolLike(line))
            {
                continue;
            }

            foreach (Match match in LatinTokenRegex.Matches(line))
            {
                if (EnglishFunctionWords.Contains(match.Value))
                {
                    return match.Value;
                }
            }
        }

        return null;
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
