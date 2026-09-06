using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryPairPreview
{
    PairPreview Preview(EpubBookModel originalBook, EpubBookModel translationBook);
}
