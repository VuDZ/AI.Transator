namespace Ai.Translator.Core.Domain;

public sealed class PairMapEntry
{
    public required string OriginalFrom { get; init; }

    public required string OriginalTo { get; init; }

    public required string TranslationFrom { get; init; }

    public required string TranslationTo { get; init; }
}
