using Ai.Translator.Core.Abstractions;

namespace Ai.Translator.Core.Translation;

public sealed class LengthTokenEstimator : ITokenEstimator
{
    public int Estimate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length / 4;
    }
}
