using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Epub;

namespace Ai.Translator.Core.Glossary;

public sealed class GlossaryPairPreview : IGlossaryPairPreview
{
    public const int PreviewLength = 80;

    public PairPreview Preview(EpubBookModel originalBook, EpubBookModel translationBook)
    {
        ArgumentNullException.ThrowIfNull(originalBook);
        ArgumentNullException.ThrowIfNull(translationBook);
        ArgumentNullException.ThrowIfNull(originalBook.Chapters);
        ArgumentNullException.ThrowIfNull(translationBook.Chapters);

        var original = ClassifyChapters(originalBook.Chapters);
        var translation = ClassifyChapters(translationBook.Chapters);
        return new PairPreview
        {
            SpinePairs = BuildSpinePairs(original, translation),
            RolePairs = BuildRolePairs(original, translation, out var originalUnpaired, out var translationUnpaired),
            OriginalUnpaired = originalUnpaired,
            TranslationUnpaired = translationUnpaired
        };
    }

    private static IReadOnlyList<SpineChapterPreview> ClassifyChapters(IReadOnlyList<EpubChapter> chapters)
    {
        var result = new List<SpineChapterPreview>(chapters.Count);
        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            ArgumentNullException.ThrowIfNull(chapter);
            ArgumentNullException.ThrowIfNull(chapter.FilePath);
            var plainText = chapter.PlainText ?? string.Empty;
            var heading = XhtmlChapterParser.GetFirstHeadingText(chapter.Xhtml ?? string.Empty);
            var previewSource = heading ?? XhtmlChapterParser.CollapseWhitespace(plainText);
            result.Add(new SpineChapterPreview
            {
                Index = i + 1,
                FilePath = chapter.FilePath,
                Preview = Truncate(previewSource),
                Role = ChapterRoleClassifier.Classify(heading, plainText),
                CharacterCount = plainText.Length
            });
        }

        return result;
    }

    private static IReadOnlyList<SpinePairRow> BuildSpinePairs(
        IReadOnlyList<SpineChapterPreview> original,
        IReadOnlyList<SpineChapterPreview> translation)
    {
        var count = Math.Max(original.Count, translation.Count);
        var rows = new List<SpinePairRow>(count);
        for (var i = 0; i < count; i++)
        {
            var left = i < original.Count ? original[i] : null;
            var right = i < translation.Count ? translation[i] : null;
            rows.Add(new SpinePairRow
            {
                Index = i + 1,
                Original = left,
                Translation = right,
                LengthRatio = Ratio(left, right)
            });
        }

        return rows;
    }

    private static double? Ratio(SpineChapterPreview? original, SpineChapterPreview? translation)
    {
        if (original is null || translation is null || original.CharacterCount <= 0)
        {
            return null;
        }

        return translation.CharacterCount / (double)original.CharacterCount;
    }

    private static IReadOnlyList<RolePairRow> BuildRolePairs(
        IReadOnlyList<SpineChapterPreview> original,
        IReadOnlyList<SpineChapterPreview> translation,
        out IReadOnlyList<UnpairedRoleRow> originalUnpaired,
        out IReadOnlyList<UnpairedRoleRow> translationUnpaired)
    {
        var originalByRole = GroupByRole(original);
        var translationByRole = GroupByRole(translation);
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in originalByRole.Keys)
        {
            keys.Add(key);
        }

        foreach (var key in translationByRole.Keys)
        {
            keys.Add(key);
        }

        var pairs = new List<RolePairRow>();
        var originalUnpairedList = new List<UnpairedRoleRow>();
        var translationUnpairedList = new List<UnpairedRoleRow>();

        foreach (var key in keys)
        {
            originalByRole.TryGetValue(key, out var originalMatches);
            translationByRole.TryGetValue(key, out var translationMatches);
            originalMatches ??= [];
            translationMatches ??= [];

            if (string.Equals(key, "other", StringComparison.Ordinal))
            {
                AddAll(originalUnpairedList, originalMatches, "role 'other' is not paired");
                AddAll(translationUnpairedList, translationMatches, "role 'other' is not paired");
                continue;
            }

            if (originalMatches.Count == 1 && translationMatches.Count == 1)
            {
                pairs.Add(new RolePairRow
                {
                    RoleKey = key,
                    OriginalPath = originalMatches[0].FilePath,
                    TranslationPath = translationMatches[0].FilePath
                });
                continue;
            }

            if (originalMatches.Count > 1)
            {
                AddAll(
                    originalUnpairedList,
                    originalMatches,
                    $"duplicate role key '{key}' on original");
            }
            else if (originalMatches.Count == 1)
            {
                AddAll(
                    originalUnpairedList,
                    originalMatches,
                    translationMatches.Count == 0
                        ? $"no counterpart for '{key}'"
                        : $"duplicate role key '{key}' on translation");
            }

            if (translationMatches.Count > 1)
            {
                AddAll(
                    translationUnpairedList,
                    translationMatches,
                    $"duplicate role key '{key}' on translation");
            }
            else if (translationMatches.Count == 1)
            {
                AddAll(
                    translationUnpairedList,
                    translationMatches,
                    originalMatches.Count == 0
                        ? $"no counterpart for '{key}'"
                        : $"duplicate role key '{key}' on original");
            }
        }

        originalUnpaired = originalUnpairedList;
        translationUnpaired = translationUnpairedList;
        return pairs;
    }

    private static Dictionary<string, List<SpineChapterPreview>> GroupByRole(
        IReadOnlyList<SpineChapterPreview> chapters)
    {
        var groups = new Dictionary<string, List<SpineChapterPreview>>(StringComparer.Ordinal);
        foreach (var chapter in chapters)
        {
            if (!groups.TryGetValue(chapter.Role, out var list))
            {
                list = [];
                groups[chapter.Role] = list;
            }

            list.Add(chapter);
        }

        return groups;
    }

    private static void AddAll(
        List<UnpairedRoleRow> target,
        List<SpineChapterPreview> chapters,
        string reason)
    {
        foreach (var chapter in chapters)
        {
            target.Add(new UnpairedRoleRow
            {
                RoleKey = chapter.Role,
                FilePath = chapter.FilePath,
                Reason = reason
            });
        }
    }

    private static string Truncate(string text)
    {
        var collapsed = XhtmlChapterParser.CollapseWhitespace(text);
        if (collapsed.Length <= PreviewLength)
        {
            return collapsed;
        }

        return collapsed[..PreviewLength];
    }
}
