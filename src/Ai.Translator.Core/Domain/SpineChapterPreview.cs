namespace Ai.Translator.Core.Domain;

public sealed class SpineChapterPreview
{
    public required int Index { get; init; }

    public required string FilePath { get; init; }

    public required string Preview { get; init; }

    public required string Role { get; init; }

    public required int CharacterCount { get; init; }
}
