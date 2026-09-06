namespace Ai.Translator.Core;

public static class InputPathGuard
{
    public const string PdfRejectedMessage =
        "PDF is not supported. Convert the book to EPUB first.";

    public static bool IsPdf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
    }
}
