using System.Globalization;
using System.Text;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Glossary;

public static class PairPreviewFormatter
{
    public static string Format(PairPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(preview.SpinePairs);
        ArgumentNullException.ThrowIfNull(preview.RolePairs);
        ArgumentNullException.ThrowIfNull(preview.OriginalUnpaired);
        ArgumentNullException.ThrowIfNull(preview.TranslationUnpaired);

        var builder = new StringBuilder();
        builder.AppendLine("Spine pairs");
        foreach (var row in preview.SpinePairs)
        {
            ArgumentNullException.ThrowIfNull(row);
            builder.Append(row.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append("  ");
            AppendSpineSide(builder, row.Original);
            builder.Append("  ");
            AppendSpineSide(builder, row.Translation);
            builder.Append("  ");
            builder.Append(FormatRatio(row.LengthRatio));
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Suggested role pairs");
        foreach (var row in preview.RolePairs)
        {
            ArgumentNullException.ThrowIfNull(row);
            builder.Append(row.RoleKey);
            builder.Append("  ");
            builder.Append(row.OriginalPath);
            builder.Append("  ");
            builder.Append(row.TranslationPath);
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("UNPAIRED original");
        AppendUnpaired(builder, preview.OriginalUnpaired);
        builder.AppendLine();
        builder.AppendLine("UNPAIRED translation");
        AppendUnpaired(builder, preview.TranslationUnpaired);
        return builder.ToString();
    }

    private static void AppendSpineSide(StringBuilder builder, SpineChapterPreview? side)
    {
        if (side is null)
        {
            builder.Append("TAIL");
            return;
        }

        builder.Append(side.FilePath);
        builder.Append("  ");
        builder.Append(side.Preview);
        builder.Append("  ");
        builder.Append(side.CharacterCount.ToString(CultureInfo.InvariantCulture));
        builder.Append("  ");
        builder.Append(side.Role);
    }

    private static void AppendUnpaired(StringBuilder builder, IReadOnlyList<UnpairedRoleRow> rows)
    {
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            builder.Append(row.RoleKey);
            builder.Append("  ");
            builder.Append(row.FilePath);
            builder.Append("  ");
            builder.Append(row.Reason);
            builder.AppendLine();
        }
    }

    private static string FormatRatio(double? ratio)
    {
        if (ratio is null)
        {
            return "—";
        }

        return ratio.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
