namespace Ai.Translator.Core.Domain;

public sealed class PairPreview
{
    public required IReadOnlyList<SpinePairRow> SpinePairs { get; init; }

    public required IReadOnlyList<RolePairRow> RolePairs { get; init; }

    public required IReadOnlyList<UnpairedRoleRow> OriginalUnpaired { get; init; }

    public required IReadOnlyList<UnpairedRoleRow> TranslationUnpaired { get; init; }
}
