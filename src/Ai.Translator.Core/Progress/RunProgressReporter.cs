using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Progress;

public sealed class RunProgressReporter
{
    private readonly IRunProgress _progress;
    private readonly TimeProvider _time;
    private readonly int _maxConcurrency;
    private readonly object _gate = new();
    private readonly List<TimeSpan> _durations = [];
    private readonly List<string> _planOrder = [];
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _started = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LlmResponse> _lastByStep = new(StringComparer.Ordinal);
    private int _total;
    private int _completed;
    private int _failed;
    private string? _lastStepId;
    private int _promptTotal;
    private int? _cachedTotal;
    private int? _completionTotal;
    private long _runStarted;

    public RunProgressReporter(IRunProgress progress, TimeProvider time, int maxConcurrency = 1)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        _progress = progress;
        _time = time;
        _maxConcurrency = maxConcurrency;
    }

    public void Begin(int totalSteps) => Begin(totalSteps, planOrder: null);

    public void Begin(int totalSteps, IReadOnlyList<string>? planOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalSteps);
        lock (_gate)
        {
            _total = totalSteps;
            _runStarted = _time.GetTimestamp();
            _planOrder.Clear();
            if (planOrder is not null)
            {
                foreach (var id in planOrder)
                {
                    ArgumentNullException.ThrowIfNull(id);
                    _planOrder.Add(id);
                }
            }

            _progress.Begin(totalSteps);
        }
    }

    public void StepBegin(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        lock (_gate)
        {
            StartStep_NoLock(label);
            _lastStepId = label;
            _progress.StepBegin(FormatInFlight_NoLock());
        }
    }

    public void RecordResponse(LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            RecordResponse_NoLock(_lastStepId ?? string.Empty, response);
        }
    }

    public void RecordResponse(string stepId, LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(stepId);
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            RecordResponse_NoLock(stepId, response);
        }
    }

    public void StepEnd(bool failed)
    {
        lock (_gate)
        {
            EndStep_NoLock(_lastStepId ?? string.Empty, failed);
        }
    }

    public void StepEnd(string stepId, bool failed)
    {
        ArgumentNullException.ThrowIfNull(stepId);
        lock (_gate)
        {
            EndStep_NoLock(stepId, failed);
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _progress.Complete(CreateTotals());
        }
    }

    private void StartStep_NoLock(string stepId)
    {
        _inFlight.Add(stepId);
        _started[stepId] = _time.GetTimestamp();
        _lastByStep.Remove(stepId);
    }

    private void RecordResponse_NoLock(string stepId, LlmResponse response)
    {
        _lastByStep[stepId] = response;
        _promptTotal += response.PromptTokens;
        _cachedTotal = AddNullable(_cachedTotal, response.CachedTokens);
        _completionTotal = AddNullable(_completionTotal, response.CompletionTokens);
    }

    private void EndStep_NoLock(string stepId, bool failed)
    {
        var started = _started.TryGetValue(stepId, out var timestamp)
            ? timestamp
            : _time.GetTimestamp();
        var elapsed = _time.GetElapsedTime(started);
        _durations.Add(elapsed);
        _completed++;
        if (failed)
        {
            _failed++;
        }

        _inFlight.Remove(stepId);
        _lastByStep.TryGetValue(stepId, out var lastResponse);

        var showAverage = _maxConcurrency > 1;
        var last = new LlmUsageSnapshot
        {
            PromptTokens = lastResponse?.PromptTokens ?? 0,
            CachedTokens = lastResponse?.CachedTokens,
            CompletionTokens = lastResponse?.CompletionTokens,
            Elapsed = showAverage ? AverageDuration() : elapsed,
            StepCount = 1,
            FailedCount = failed ? 1 : 0,
            ElapsedIsAverage = showAverage
        };

        var remaining = Math.Max(0, _total - _completed);
        _progress.StepEnd(last, CreateTotals(), ComputeEta(remaining));
        if (_inFlight.Count > 0)
        {
            _progress.StepBegin(FormatInFlight_NoLock());
        }
    }

    private string FormatInFlight_NoLock()
    {
        if (_planOrder.Count > 0)
        {
            var ordered = new List<string>(_inFlight.Count);
            foreach (var id in _planOrder)
            {
                if (_inFlight.Contains(id))
                {
                    ordered.Add(id);
                }
            }

            return string.Join(",", ordered);
        }

        return string.Join(",", _inFlight);
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
        var width = Math.Max(1, Math.Min(_maxConcurrency, remaining));
        var waves = (int)Math.Ceiling(remaining / (double)width);
        return TimeSpan.FromTicks(checked((long)(averageTicks * waves)));
    }

    private TimeSpan AverageDuration()
    {
        if (_durations.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var averageTicks = 0d;
        foreach (var duration in _durations)
        {
            averageTicks += duration.Ticks;
        }

        averageTicks /= _durations.Count;
        return TimeSpan.FromTicks(checked((long)averageTicks));
    }

    private LlmUsageSnapshot CreateTotals() => new()
    {
        PromptTokens = _promptTotal,
        CachedTokens = _cachedTotal,
        CompletionTokens = _completionTotal,
        Elapsed = _time.GetElapsedTime(_runStarted),
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
