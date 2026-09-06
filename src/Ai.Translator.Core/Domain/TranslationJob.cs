namespace Ai.Translator.Core.Domain;

public sealed class TranslationJob
{
    public required string InputPath { get; init; }

    public required string GlossaryPath { get; init; }

    public required string OutputPath { get; init; }

    public string? Model { get; init; }

    public string? WorkDir { get; init; }

    public bool Resume { get; init; }

    public string? Chapters { get; init; }
}
