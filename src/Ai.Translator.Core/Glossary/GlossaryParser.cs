using System.Text;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryParser : IGlossaryParser
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ru",
        "type",
        "notes",
        "do-not-use",
        "aliases"
    };

    private readonly ILogger<GlossaryParser> _logger;

    public GlossaryParser(ILogger<GlossaryParser>? logger = null)
    {
        _logger = logger ?? NullLogger<GlossaryParser>.Instance;
    }

    public GlossaryDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new GlossaryFormatException("Corpus is empty.");
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        string? title = null;
        var preambleLines = new List<string>();
        var entries = new List<GlossaryEntry>();
        string? currentEnglish = null;
        Dictionary<string, string>? currentFields = null;
        var seenEntry = false;

        foreach (var line in lines)
        {
            if (IsEntryHeading(line))
            {
                FlushEntry(entries, currentEnglish, currentFields);
                seenEntry = true;
                currentEnglish = line[2..].Trim();
                if (currentEnglish.Length == 0)
                {
                    throw new GlossaryFormatException("Corpus Markdown is invalid: empty ## heading.");
                }

                currentFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (!seenEntry && IsTitleHeading(line))
            {
                if (title is null)
                {
                    title = line[1..].Trim();
                }
                else
                {
                    preambleLines.Add(line);
                }

                continue;
            }

            if (!seenEntry)
            {
                preambleLines.Add(line);
                continue;
            }

            if (TryParseField(line, out var key, out var value))
            {
                if (KnownKeys.Contains(key))
                {
                    currentFields![key] = value;
                }
                else
                {
                    _logger.LogWarning(
                        "Ignoring unknown glossary field '{Field}' on entry '{Entry}'.",
                        key,
                        currentEnglish);
                }
            }
        }

        FlushEntry(entries, currentEnglish, currentFields);

        if (entries.Count == 0)
        {
            throw new GlossaryFormatException(
                "Corpus Markdown is invalid: no glossary entries (## headings).");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new GlossaryFormatException("Corpus Markdown is invalid: missing document title (# heading).");
        }

        return new GlossaryDocument
        {
            Title = title,
            Preamble = TrimPreamble(preambleLines),
            Entries = entries
        };
    }

    private static void FlushEntry(
        List<GlossaryEntry> entries,
        string? english,
        Dictionary<string, string>? fields)
    {
        if (english is null || fields is null)
        {
            return;
        }

        if (!fields.TryGetValue("ru", out var ru) || string.IsNullOrWhiteSpace(ru))
        {
            throw new GlossaryFormatException(
                $"Glossary entry '{english}' is missing required field 'ru'.");
        }

        fields.TryGetValue("notes", out var notes);
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        entries.Add(new GlossaryEntry
        {
            English = english,
            Ru = ru.Trim(),
            Kind = ParseKind(fields.GetValueOrDefault("type")),
            Notes = trimmedNotes,
            DoNotUse = SplitList(fields.GetValueOrDefault("do-not-use")),
            Aliases = SplitList(fields.GetValueOrDefault("aliases"))
        });
    }

    private static bool IsTitleHeading(string line) =>
        line.StartsWith('#') && !line.StartsWith("##", StringComparison.Ordinal);

    private static bool IsEntryHeading(string line) =>
        line.StartsWith("##", StringComparison.Ordinal)
        && (line.Length == 2 || line[2] != '#');

    private static bool TryParseField(string line, out string key, out string value)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        var rest = trimmed[2..];
        var colon = rest.IndexOf(':');
        if (colon <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = rest[..colon].Trim();
        value = rest[(colon + 1)..].Trim();
        return key.Length > 0;
    }

    private static GlossaryEntryKind ParseKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GlossaryEntryKind.Other;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "name" => GlossaryEntryKind.Name,
            "title" => GlossaryEntryKind.Title,
            "organization" => GlossaryEntryKind.Organization,
            "artifact" => GlossaryEntryKind.Artifact,
            "geography" => GlossaryEntryKind.Geography,
            "formula" => GlossaryEntryKind.Formula,
            "other" => GlossaryEntryKind.Other,
            _ => GlossaryEntryKind.Other
        };
    }

    private static IReadOnlyList<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string TrimPreamble(List<string> lines)
    {
        var start = 0;
        var end = lines.Count - 1;
        while (start <= end && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        if (start > end)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = start; i <= end; i++)
        {
            if (i > start)
            {
                sb.Append('\n');
            }

            sb.Append(lines[i]);
        }

        return sb.ToString();
    }
}
