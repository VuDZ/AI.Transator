namespace Ai.Translator.Core.Domain;

public sealed class ChapterRange
{
    public ChapterRange(int from, int to)
    {
        if (from < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }

        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to));
        }

        From = from;
        To = to;
    }

    public int From { get; }

    public int To { get; }

    public bool Contains(int chapterNumber) => chapterNumber >= From && chapterNumber <= To;
}
