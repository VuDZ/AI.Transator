namespace Ai.Translator.Core.Glossary;

public static class GlossaryTermMatcher
{
    public static bool AppearsIn(string bookPlainText, string term)
    {
        ArgumentNullException.ThrowIfNull(bookPlainText);
        ArgumentNullException.ThrowIfNull(term);

        if (term.Length == 0 || bookPlainText.Length < term.Length)
        {
            return false;
        }

        var start = 0;
        while (start <= bookPlainText.Length - term.Length)
        {
            var index = bookPlainText.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var leftOk = index == 0 || !IsAsciiLetter(bookPlainText[index - 1]);
            var after = index + term.Length;
            var rightOk = after == bookPlainText.Length || !IsAsciiLetter(bookPlainText[after]);
            if (leftOk && rightOk)
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }

    private static bool IsAsciiLetter(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
}
