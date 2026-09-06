using System.IO.Compression;
using System.Text;

namespace Ai.Translator.Tests;

internal static class MinimalEpubFactory
{
    public static string Create(string directory, string bodyInnerHtml)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(bodyInnerHtml);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "book.epub");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            WriteEntry(
                zip,
                "META-INF/container.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """.Trim(),
                CompressionLevel.Optimal);
            WriteEntry(
                zip,
                "OEBPS/content.opf",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="BookId">urn:uuid:11111111-1111-1111-1111-111111111111</dc:identifier>
                    <dc:title>Test Book</dc:title>
                    <dc:language>en</dc:language>
                  </metadata>
                  <manifest>
                    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                    <item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/>
                  </manifest>
                  <spine>
                    <itemref idref="chapter"/>
                  </spine>
                </package>
                """.Trim(),
                CompressionLevel.Optimal);
            WriteEntry(
                zip,
                "OEBPS/nav.xhtml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
                  <head><title>Nav</title></head>
                  <body>
                    <nav epub:type="toc">
                      <ol>
                        <li><a href="chapter.xhtml">Chapter</a></li>
                      </ol>
                    </nav>
                  </body>
                </html>
                """.Trim(),
                CompressionLevel.Optimal);
            WriteEntry(
                zip,
                "OEBPS/chapter.xhtml",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <head><title>Chapter</title></head>
                  <body>{bodyInnerHtml}</body>
                </html>
                """.Trim(),
                CompressionLevel.Optimal);
        }

        return path;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content, CompressionLevel level)
    {
        var entry = zip.CreateEntry(name, level);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
