namespace Ai.Translator.Core.Domain;

public sealed class EpubChapter
{
    public required string FilePath { get; init; }

    public required string Xhtml { get; init; }

    public required string PlainText { get; init; }

    public required string BodyInnerHtml { get; init; }

    public IReadOnlyList<string> BlockFragments { get; init; } = [];
}
