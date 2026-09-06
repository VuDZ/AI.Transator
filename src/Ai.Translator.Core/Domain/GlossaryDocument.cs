namespace Ai.Translator.Core.Domain;

public sealed class GlossaryDocument
{
    public required string Title { get; init; }

    public string Preamble { get; init; } = string.Empty;

    public IReadOnlyList<GlossaryEntry> Entries { get; init; } = [];
}
