using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Progress;

public sealed class NullRunProgress : IRunProgress
{
    public static NullRunProgress Instance { get; } = new();

    public void Begin(int totalSteps)
    {
    }

    public void StepBegin(string label)
    {
    }

    public void StepEnd(LlmUsageSnapshot last, LlmUsageSnapshot totals, TimeSpan etaOrZero)
    {
    }

    public void Complete(LlmUsageSnapshot totals)
    {
    }
}
