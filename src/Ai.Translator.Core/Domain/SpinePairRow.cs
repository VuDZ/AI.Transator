namespace Ai.Translator.Core.Domain;

public sealed class SpinePairRow
{
    public required int Index { get; init; }

    public SpineChapterPreview? Original { get; init; }

    public SpineChapterPreview? Translation { get; init; }

    public double? LengthRatio { get; init; }

    public bool IsTail => Original is null || Translation is null;
}
