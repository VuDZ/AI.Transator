using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface ITranslationValidator
{
    ValidationResult Validate(string sourceChunk, LlmResponse response);
}
