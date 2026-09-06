namespace Ai.Translator.Core.Domain;

public sealed class EpubReplace
{
    public required string FilePath { get; init; }

    public required string BodyInnerHtml { get; init; }
}
