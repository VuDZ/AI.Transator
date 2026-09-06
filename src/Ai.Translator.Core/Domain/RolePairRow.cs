namespace Ai.Translator.Core.Domain;

public sealed class RolePairRow
{
    public required string RoleKey { get; init; }

    public required string OriginalPath { get; init; }

    public required string TranslationPath { get; init; }
}
