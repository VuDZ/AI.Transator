using System.IO.Compression;
using System.Text;

namespace Ai.Translator.Tests;

internal static class MinimalEpubFactory
{
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    public static string Create(string directory, params string[] chapterBodyInnerHtml)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(chapterBodyInnerHtml);
        if (chapterBodyInnerHtml.Length == 0)
        {
            throw new ArgumentException("At least one chapter is required.", nameof(chapterBodyInnerHtml));
        }

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
            WriteEntry(zip, "OEBPS/content.opf", BuildOpf(chapterBodyInnerHtml.Length), CompressionLevel.Optimal);
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
                        <li><a href="chapter1.xhtml">Chapter</a></li>
                      </ol>
                    </nav>
                  </body>
                </html>
                """.Trim(),
                CompressionLevel.Optimal);
            WriteEntry(zip, "OEBPS/styles/style.css", "body { margin: 0; }", CompressionLevel.Optimal);
            WriteBinaryEntry(zip, "OEBPS/images/pixel.png", OnePixelPng);

            for (var i = 0; i < chapterBodyInnerHtml.Length; i++)
            {
                var body = chapterBodyInnerHtml[i]
                    ?? throw new ArgumentNullException(nameof(chapterBodyInnerHtml));
                var index = i + 1;
                WriteEntry(
                    zip,
                    $"OEBPS/chapter{index}.xhtml",
                    $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml">
                      <head>
                        <title>Chapter {index}</title>
                        <link rel="stylesheet" type="text/css" href="styles/style.css"/>
                      </head>
                      <body>{body}</body>
                    </html>
                    """.Trim(),
                    CompressionLevel.Optimal);
            }
        }

        return path;
    }

    private static string BuildOpf(int chapterCount)
    {
        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        for (var i = 1; i <= chapterCount; i++)
        {
            manifest.AppendLine(
                $"    <item id=\"chapter{i}\" href=\"chapter{i}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spine.AppendLine($"    <itemref idref=\"chapter{i}\"/>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="BookId">urn:uuid:11111111-1111-1111-1111-111111111111</dc:identifier>
                <dc:title>Test Book</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="css" href="styles/style.css" media-type="text/css"/>
                <item id="pixel" href="images/pixel.png" media-type="image/png"/>
            {manifest}  </manifest>
              <spine>
            {spine}  </spine>
            </package>
            """.Trim();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content, CompressionLevel level)
    {
        var entry = zip.CreateEntry(name, level);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void WriteBinaryEntry(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
