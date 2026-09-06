using System.IO.Compression;

namespace Ai.Translator.Core.Epub;

internal static class EpubEntryPath
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Replace('\\', '/').Trim('/');
    }

    public static ZipArchiveEntry? Find(ZipArchive archive, string filePath)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(filePath);

        var normalized = Normalize(filePath);
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(Normalize(entry.FullName), normalized, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }
}
