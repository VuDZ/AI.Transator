using System.IO.Compression;
using System.Text;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using VersOne.Epub;

namespace Ai.Translator.Core.Epub;

public sealed class EpubBookService : IEpubBookService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<EpubBookModel> OpenAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            await using var fileStream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var book = await EpubReader.ReadBookAsync(fileStream).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(book);

            var readingOrder = book.ReadingOrder;
            var chapters = new List<EpubChapter>(readingOrder.Count);
            foreach (var item in readingOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var xhtml = item.Content;
                if (string.IsNullOrWhiteSpace(xhtml))
                {
                    continue;
                }

                var plainText = XhtmlToPlainText.Convert(xhtml);
                if (string.IsNullOrWhiteSpace(plainText))
                {
                    continue;
                }

                chapters.Add(new EpubChapter
                {
                    FilePath = EpubEntryPath.Normalize(item.FilePath),
                    Xhtml = xhtml,
                    PlainText = plainText,
                    BodyInnerHtml = XhtmlChapterParser.GetBodyInnerHtml(xhtml),
                    BlockFragments = XhtmlChapterParser.GetBlockFragments(xhtml)
                });
            }

            return new EpubBookModel { Chapters = chapters };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ArgumentNullException)
        {
            throw new InvalidOperationException($"Failed to read EPUB: {ex.Message}", ex);
        }
    }

    public async Task WriteCopyAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<EpubReplace> replacements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(replacements);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Update);
            foreach (var replacement in replacements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(replacement);
                ArgumentNullException.ThrowIfNull(replacement.FilePath);
                ArgumentNullException.ThrowIfNull(replacement.BodyInnerHtml);

                var entry = EpubEntryPath.Find(archive, replacement.FilePath)
                    ?? throw new InvalidOperationException(
                        $"EPUB entry was not found: {replacement.FilePath}");
                var entryName = entry.FullName;
                if (string.Equals(EpubEntryPath.Normalize(entryName), "mimetype", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The mimetype entry must not be replaced.");
                }

                string originalXhtml;
                await using (var entryStream = entry.Open())
                using (var reader = new StreamReader(
                    entryStream,
                    Utf8NoBom,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true))
                {
                    originalXhtml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                }

                var updatedXhtml = XhtmlChapterParser.ReplaceBodyInnerHtml(
                    originalXhtml,
                    replacement.BodyInnerHtml);
                entry.Delete();

                var created = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using (var createdStream = created.Open())
                await using (var writer = new StreamWriter(
                    createdStream,
                    Utf8NoBom,
                    bufferSize: 1024,
                    leaveOpen: true))
                {
                    await writer.WriteAsync(updatedXhtml.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ArgumentNullException and not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to write EPUB copy: {ex.Message}", ex);
        }
    }
}
