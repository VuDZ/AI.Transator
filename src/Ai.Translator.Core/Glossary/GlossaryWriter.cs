using System.Text;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryWriter : IGlossaryWriter
{
    public string Write(GlossaryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Title);
        ArgumentNullException.ThrowIfNull(document.Entries);

        var sb = new StringBuilder();
        sb.Append("# ").Append(document.Title).Append('\n');

        if (!string.IsNullOrEmpty(document.Preamble))
        {
            sb.Append('\n');
            sb.Append(document.Preamble);
            if (!document.Preamble.EndsWith('\n'))
            {
                sb.Append('\n');
            }
        }

        foreach (var entry in document.Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            sb.Append('\n');
            sb.Append("## ").Append(entry.English).Append('\n');
            sb.Append("- ru: ").Append(entry.Ru).Append('\n');
            sb.Append("- type: ").Append(FormatKind(entry.Kind)).Append('\n');
            if (!string.IsNullOrWhiteSpace(entry.Notes))
            {
                sb.Append("- notes: ").Append(entry.Notes).Append('\n');
            }

            if (entry.DoNotUse.Count > 0)
            {
                sb.Append("- do-not-use: ").Append(string.Join(", ", entry.DoNotUse)).Append('\n');
            }

            if (entry.Aliases.Count > 0)
            {
                sb.Append("- aliases: ").Append(string.Join(", ", entry.Aliases)).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string FormatKind(GlossaryEntryKind kind) => kind switch
    {
        GlossaryEntryKind.Name => "name",
        GlossaryEntryKind.Title => "title",
        GlossaryEntryKind.Organization => "organization",
        GlossaryEntryKind.Artifact => "artifact",
        GlossaryEntryKind.Geography => "geography",
        GlossaryEntryKind.Formula => "formula",
        _ => "other"
    };
}
