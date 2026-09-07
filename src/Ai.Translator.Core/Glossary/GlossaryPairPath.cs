namespace Ai.Translator.Core.Glossary;

internal static class GlossaryPairPath
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Replace('\\', '/').Trim().Trim('/');
    }

    public static bool AreEqual(string left, string right)
    {
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }
}
