using System.Text;
using Ai.Translator.Core.Abstractions;
using VersOne.Epub;

namespace Ai.Translator.Core.Epub;

public sealed class EpubBookTextExtractor : IBookTextExtractor
{
    public async Task<string> ExtractPlainTextAsync(string epubPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(epubPath);

        try
        {
            await using var fileStream = new FileStream(
                epubPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var memory = new MemoryStream();
            await fileStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            memory.Position = 0;

            var book = await EpubReader.ReadBookAsync(memory).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(book);

            var readingOrder = book.ReadingOrder;
            var builder = new StringBuilder();
            foreach (var item in readingOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var html = item.Content;
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                var text = XhtmlToPlainText.Convert(html);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(text);
            }

            return builder.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read EPUB: {ex.Message}", ex);
        }
    }
}
