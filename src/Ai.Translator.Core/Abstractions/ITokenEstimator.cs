namespace Ai.Translator.Core.Abstractions;

public interface ITokenEstimator
{
    int Estimate(string text);
}
