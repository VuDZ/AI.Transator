namespace Ai.Translator.Core.Glossary;

public static class GlossaryExtractArguments
{
    public const string ListPairsConflictMessage =
        "--list-pairs cannot be combined with --out, --merge-into, or --model.";

    public static bool HasListPairsConflict(
        bool listPairs,
        string? output,
        string? mergeInto,
        string? model)
    {
        if (!listPairs)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(output)
            || !string.IsNullOrWhiteSpace(mergeInto)
            || !string.IsNullOrWhiteSpace(model);
    }
}
