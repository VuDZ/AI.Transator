using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface ILlmProvider
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
