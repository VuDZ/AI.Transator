namespace Ai.Translator.Core.Domain;

public sealed class GlossaryEntry
{
    public required string English { get; init; }

    public required string Ru { get; init; }

    public GlossaryEntryKind Kind { get; init; } = GlossaryEntryKind.Other;

    public string? Notes { get; init; }

    public IReadOnlyList<string> DoNotUse { get; init; } = [];

    public IReadOnlyList<string> Aliases { get; init; } = [];
}
