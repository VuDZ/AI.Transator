using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Abstractions;

public interface IRunProgress
{
    void Begin(int totalSteps);

    void StepBegin(string label);

    void StepEnd(LlmUsageSnapshot last, LlmUsageSnapshot totals, TimeSpan etaOrZero);

    void Complete(LlmUsageSnapshot totals);
}
