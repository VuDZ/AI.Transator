namespace Ai.Translator.Core.Glossary;

public static class GlossaryExtractArguments
{
    public const string ListPairsConflictMessage =
        "--list-pairs cannot be combined with --out, --merge-into, or --model.";

    public const string ListPairsPairsConflictMessage =
        "--list-pairs cannot be combined with --pairs.";

    public const string PairsRequiresOutMessage =
        "--pairs requires --out.";

    public const string MaxPairsRequiresPairsMessage =
        "--max-pairs requires --pairs.";

    public const string MaxPairsMustBePositiveMessage =
        "--max-pairs must be a positive integer.";

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

    public static bool HasListPairsPairsConflict(bool listPairs, string? pairs)
    {
        return listPairs && !string.IsNullOrWhiteSpace(pairs);
    }

    public static bool HasMaxPairsWithoutPairs(int? maxPairs, string? pairs)
    {
        return maxPairs is not null && string.IsNullOrWhiteSpace(pairs);
    }

    public static bool HasInvalidMaxPairs(int? maxPairs)
    {
        return maxPairs is <= 0;
    }

    public static bool HasPairsWithoutOut(string? pairs, string? output)
    {
        return !string.IsNullOrWhiteSpace(pairs) && string.IsNullOrWhiteSpace(output);
    }
}
