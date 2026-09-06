using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IEpubBookService
{
    Task<EpubBookModel> OpenAsync(string path, CancellationToken cancellationToken);

    Task WriteCopyAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<EpubReplace> replacements,
        CancellationToken cancellationToken);
}
