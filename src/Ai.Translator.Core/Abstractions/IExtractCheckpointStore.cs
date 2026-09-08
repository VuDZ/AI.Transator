using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IExtractCheckpointStore
{
    Task<ExtractCheckpointState?> LoadAsync(string workDir, CancellationToken cancellationToken);

    Task SaveAsync(string workDir, ExtractCheckpointState state, CancellationToken cancellationToken);
}
