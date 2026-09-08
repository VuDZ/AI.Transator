using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Progress;

namespace Ai.Translator.Tests;

public sealed class RunProgressReporterTests
{
    [Fact]
    public void Eta_BeforeFirstStepEnd_IsNotReportedPositive()
    {
        var progress = new RecordingRunProgress();
        var time = new ManualTimeProvider();
        var reporter = new RunProgressReporter(progress, time);

        reporter.Begin(2);
        reporter.StepBegin("0001-0000");
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(2, progress.TotalSteps);
        Assert.Equal("0001-0000", Assert.Single(progress.Labels));
        Assert.Empty(progress.Ends);
        Assert.False(progress.Completed);
    }

    [Fact]
    public void Eta_AfterFirstOfTwoSteps_IsAverageTimesRemaining()
    {
        var progress = new RecordingRunProgress();
        var time = new ManualTimeProvider();
        var reporter = new RunProgressReporter(progress, time);

        reporter.Begin(2);
        reporter.StepBegin("a");
        time.Advance(TimeSpan.FromSeconds(2));
        reporter.RecordResponse(new LlmResponse { PromptTokens = 10, CompletionTokens = 3 });
        reporter.StepEnd(failed: false);

        var first = Assert.Single(progress.Ends);
        Assert.Equal(TimeSpan.FromSeconds(2), first.Eta);
        Assert.Equal(10, first.Last.PromptTokens);
        Assert.Equal(10, first.Totals.PromptTokens);
        Assert.Equal(3, first.Totals.CompletionTokens);
        Assert.Equal(1, first.Totals.StepCount);
        Assert.Equal(0, first.Totals.FailedCount);
        Assert.True(first.Eta > TimeSpan.Zero);
    }

    [Fact]
    public void Eta_AfterLastStep_IsZeroAndTotalsSumPromptTokens()
    {
        var progress = new RecordingRunProgress();
        var time = new ManualTimeProvider();
        var reporter = new RunProgressReporter(progress, time);

        reporter.Begin(2);
        reporter.StepBegin("a");
        time.Advance(TimeSpan.FromSeconds(2));
        reporter.RecordResponse(new LlmResponse { PromptTokens = 10, CachedTokens = 4, CompletionTokens = 2 });
        reporter.StepEnd(failed: false);

        reporter.StepBegin("b");
        time.Advance(TimeSpan.FromSeconds(4));
        reporter.RecordResponse(new LlmResponse { PromptTokens = 15, CachedTokens = 1, CompletionTokens = 5 });
        reporter.StepEnd(failed: false);
        reporter.Complete();

        Assert.Equal(2, progress.Ends.Count);
        Assert.Equal(TimeSpan.Zero, progress.Ends[1].Eta);
        Assert.Equal(25, progress.Ends[1].Totals.PromptTokens);
        Assert.Equal(5, progress.Ends[1].Totals.CachedTokens);
        Assert.Equal(7, progress.Ends[1].Totals.CompletionTokens);
        Assert.Equal(2, progress.Ends[1].Totals.StepCount);
        Assert.Equal(TimeSpan.FromSeconds(6), progress.Ends[1].Totals.Elapsed);
        Assert.Equal(25, progress.CompletedTotals?.PromptTokens);
        Assert.True(progress.Completed);
    }

    [Fact]
    public void StepEnd_Failed_IncrementsFailedCount()
    {
        var progress = new RecordingRunProgress();
        var reporter = new RunProgressReporter(progress, new ManualTimeProvider());

        reporter.Begin(1);
        reporter.StepBegin("x");
        reporter.StepEnd(failed: true);
        reporter.Complete();

        Assert.Equal(1, progress.Ends[0].Last.FailedCount);
        Assert.Equal(1, progress.Ends[0].Totals.FailedCount);
        Assert.Equal(1, progress.CompletedTotals?.FailedCount);
    }

    [Fact]
    public void Eta_AfterWarmupWithConcurrency2_UsesWaveCount()
    {
        var progress = new RecordingRunProgress();
        var time = new ManualTimeProvider();
        var reporter = new RunProgressReporter(progress, time, maxConcurrency: 2);

        reporter.Begin(3);
        reporter.StepBegin("a");
        time.Advance(TimeSpan.FromSeconds(2));
        reporter.RecordResponse(new LlmResponse { PromptTokens = 10, CompletionTokens = 3 });
        reporter.StepEnd(failed: false);

        var first = Assert.Single(progress.Ends);
        Assert.Equal(TimeSpan.FromSeconds(2), first.Eta);
    }

    [Fact]
    public void TotalsElapsed_OverlappingSteps_IsWallClockNotSum()
    {
        var progress = new RecordingRunProgress();
        var time = new ManualTimeProvider();
        var reporter = new RunProgressReporter(progress, time, maxConcurrency: 2);

        reporter.Begin(2);
        reporter.StepBegin("a");
        reporter.StepBegin("b");
        time.Advance(TimeSpan.FromSeconds(2));
        reporter.StepEnd("a", failed: false);
        time.Advance(TimeSpan.FromSeconds(3));
        reporter.StepEnd("b", failed: false);
        reporter.Complete();

        Assert.Equal(TimeSpan.FromSeconds(5), progress.Ends[1].Totals.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(5), progress.CompletedTotals?.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(2), progress.Ends[0].Last.Elapsed);
        Assert.Equal(TimeSpan.FromTicks((TimeSpan.FromSeconds(2).Ticks + TimeSpan.FromSeconds(5).Ticks) / 2), progress.Ends[1].Last.Elapsed);
        Assert.True(progress.Ends[1].Last.ElapsedIsAverage);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utc;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _utc.UtcTicks;

        public void Advance(TimeSpan delta) => _utc += delta;
    }
}
