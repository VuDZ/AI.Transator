using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface ICheckpointStore
{
    Task<TranslationCheckpointState?> LoadAsync(string workDir, CancellationToken cancellationToken);

    Task SaveAsync(string workDir, TranslationCheckpointState state, CancellationToken cancellationToken);

    Task WriteChunkSourceAsync(string workDir, string chunkId, string html, CancellationToken cancellationToken);

    Task WriteChunkTranslatedAsync(string workDir, string chunkId, string html, CancellationToken cancellationToken);

    Task WriteChunkErrorAsync(string workDir, string chunkId, string error, CancellationToken cancellationToken);

    Task<string?> ReadChunkTranslatedAsync(string workDir, string chunkId, CancellationToken cancellationToken);

    Task WriteCandidatesAsync(string workDir, string markdown, CancellationToken cancellationToken);

    Task<string?> ReadCandidatesAsync(string workDir, CancellationToken cancellationToken);
}
