namespace Ai.Translator.Core.Domain;

public sealed class UnpairedRoleRow
{
    public required string RoleKey { get; init; }

    public required string FilePath { get; init; }

    public required string Reason { get; init; }
}
