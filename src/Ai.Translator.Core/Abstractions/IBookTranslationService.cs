using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IBookTranslationService
{
    Task<TranslationResult> RunAsync(TranslationJob job, CancellationToken cancellationToken);
}
