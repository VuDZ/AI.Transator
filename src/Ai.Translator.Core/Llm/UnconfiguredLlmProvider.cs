using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Llm;

public sealed class UnconfiguredLlmProvider : ILlmProvider
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        throw new InvalidOperationException(
            "Chat Completions client is not configured. Tests should inject a mock ILlmProvider.");
    }
}
