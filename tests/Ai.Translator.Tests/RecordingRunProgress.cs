using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Tests;

internal sealed class RecordingRunProgress : IRunProgress
{
    public int? TotalSteps { get; private set; }

    public List<string> Labels { get; } = [];

    public List<StepEndRecord> Ends { get; } = [];

    public LlmUsageSnapshot? CompletedTotals { get; private set; }

    public bool Completed { get; private set; }

    public void Begin(int totalSteps) => TotalSteps = totalSteps;

    public void StepBegin(string label) => Labels.Add(label);

    public void StepEnd(LlmUsageSnapshot last, LlmUsageSnapshot totals, TimeSpan etaOrZero)
    {
        ArgumentNullException.ThrowIfNull(last);
        ArgumentNullException.ThrowIfNull(totals);
        Ends.Add(new StepEndRecord(last, totals, etaOrZero));
    }

    public void Complete(LlmUsageSnapshot totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        Completed = true;
        CompletedTotals = totals;
    }
}

internal sealed record StepEndRecord(LlmUsageSnapshot Last, LlmUsageSnapshot Totals, TimeSpan Eta);
