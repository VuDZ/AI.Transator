using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Progress;

public sealed class RunProgressReporter
{
    private readonly IRunProgress _progress;
    private readonly TimeProvider _time;
    private readonly List<TimeSpan> _durations = [];
    private int _total;
    private int _completed;
    private int _failed;
    private long _stepStarted;
    private LlmResponse? _lastResponse;
    private int _promptTotal;
    private int? _cachedTotal;
    private int? _completionTotal;
    private TimeSpan _elapsedTotal;

    public RunProgressReporter(IRunProgress progress, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(time);
        _progress = progress;
        _time = time;
    }

    public void Begin(int totalSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalSteps);
        _total = totalSteps;
        _progress.Begin(totalSteps);
    }

    public void StepBegin(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        _lastResponse = null;
        _stepStarted = _time.GetTimestamp();
        _progress.StepBegin(label);
    }

    public void RecordResponse(LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _lastResponse = response;
        _promptTotal += response.PromptTokens;
        _cachedTotal = AddNullable(_cachedTotal, response.CachedTokens);
        _completionTotal = AddNullable(_completionTotal, response.CompletionTokens);
    }

    public void StepEnd(bool failed)
    {
        var elapsed = _time.GetElapsedTime(_stepStarted);
        _durations.Add(elapsed);
        _elapsedTotal += elapsed;
        _completed++;
        if (failed)
        {
            _failed++;
        }

        var last = new LlmUsageSnapshot
        {
            PromptTokens = _lastResponse?.PromptTokens ?? 0,
            CachedTokens = _lastResponse?.CachedTokens,
            CompletionTokens = _lastResponse?.CompletionTokens,
            Elapsed = elapsed,
            StepCount = 1,
            FailedCount = failed ? 1 : 0
        };

        var remaining = Math.Max(0, _total - _completed);
        _progress.StepEnd(last, CreateTotals(), ComputeEta(remaining));
    }

    public void Complete()
    {
        _progress.Complete(CreateTotals());
    }

    private TimeSpan ComputeEta(int remaining)
    {
        if (_durations.Count == 0 || remaining == 0)
        {
            return TimeSpan.Zero;
        }

        var averageTicks = 0d;
        foreach (var duration in _durations)
        {
            averageTicks += duration.Ticks;
        }

        averageTicks /= _durations.Count;
        return TimeSpan.FromTicks(checked((long)(averageTicks * remaining)));
    }

    private LlmUsageSnapshot CreateTotals() => new()
    {
        PromptTokens = _promptTotal,
        CachedTokens = _cachedTotal,
        CompletionTokens = _completionTotal,
        Elapsed = _elapsedTotal,
        StepCount = _completed,
        FailedCount = _failed
    };

    private static int? AddNullable(int? accumulated, int? value)
    {
        if (accumulated is null && value is null)
        {
            return null;
        }

        return (accumulated ?? 0) + (value ?? 0);
    }
}
