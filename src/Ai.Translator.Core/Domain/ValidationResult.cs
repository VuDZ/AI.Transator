namespace Ai.Translator.Core.Domain;

public sealed class ValidationResult
{
    public bool IsValid { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static ValidationResult Ok(IReadOnlyList<string>? warnings = null) =>
        new() { IsValid = true, Warnings = warnings ?? [] };

    public static ValidationResult Fail(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new ValidationResult { IsValid = false, Reason = reason };
    }
}
