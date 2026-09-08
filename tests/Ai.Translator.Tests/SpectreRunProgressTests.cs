using Ai.Translator.Cli;
using Ai.Translator.Core.Domain;
using Spectre.Console;

namespace Ai.Translator.Tests;

public sealed class SpectreRunProgressTests
{
    [Fact]
    public void NonInteractive_TwoSteps_DoesNotRepeatUsageTable()
    {
        var (console, writer) = CreateConsole(interactive: false);
        using var progress = new SpectreRunProgress(console);

        progress.Begin(2);
        progress.StepBegin("chapter-a.xhtml");
        progress.StepEnd(Snapshot(10, 1), Snapshot(10, 1), TimeSpan.FromSeconds(3));
        progress.StepBegin("chapter-b.xhtml");
        progress.StepEnd(Snapshot(15, 2), Snapshot(25, 2), TimeSpan.Zero);
        progress.Complete(Snapshot(25, 2));

        var dump = writer.ToString();
        Assert.DoesNotContain("…", dump);
        Assert.Contains("chapter-a.xhtml", dump, StringComparison.Ordinal);
        Assert.Contains("chapter-b.xhtml", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("last", dump, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(dump, "elapsed"));
    }

    [Fact]
    public void Interactive_TwoSteps_RewritesInPlaceWithoutEllipsisTape()
    {
        var (console, writer) = CreateConsole(interactive: true);
        using var progress = new SpectreRunProgress(console);

        progress.Begin(2);
        progress.StepBegin("chapter-a.xhtml");
        progress.StepEnd(Snapshot(10, 1), Snapshot(10, 1), TimeSpan.FromSeconds(3));
        progress.StepBegin("chapter-b.xhtml");
        progress.StepEnd(Snapshot(15, 2), Snapshot(25, 2), TimeSpan.Zero);
        progress.Complete(Snapshot(25, 2));

        var dump = writer.ToString();
        Assert.DoesNotContain("…", dump);
        Assert.Contains("chapter-a.xhtml", dump, StringComparison.Ordinal);
        Assert.Contains("chapter-b.xhtml", dump, StringComparison.Ordinal);
        Assert.Contains("\u001b[", dump, StringComparison.Ordinal);
        Assert.Contains("last", dump, StringComparison.Ordinal);
        Assert.True(
            System.Text.RegularExpressions.Regex.IsMatch(dump, "\u001b\\[[2-9]\\d*A"),
            "After the usage table is shown, the next redraw must cursor-up more than one line.");
    }

    [Fact]
    public void Interactive_AverageElapsed_ShowsAvgRowNotLast()
    {
        var (console, writer) = CreateConsole(interactive: true);
        using var progress = new SpectreRunProgress(console);

        progress.Begin(2);
        progress.StepBegin("0001-0000,0002-0000");
        progress.StepEnd(
            new LlmUsageSnapshot
            {
                PromptTokens = 10,
                CachedTokens = 1,
                CompletionTokens = 2,
                Elapsed = TimeSpan.FromSeconds(90),
                StepCount = 1,
                FailedCount = 0,
                ElapsedIsAverage = true
            },
            Snapshot(20, 2),
            TimeSpan.FromSeconds(30));
        progress.Complete(Snapshot(20, 2));

        var dump = writer.ToString();
        Assert.Contains("avg", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("last", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Interactive_LongInFlightLabel_TruncatesInsteadOfWrapping()
    {
        var (console, writer) = CreateConsole(interactive: true);
        console.Profile.Width = 80;
        using var progress = new SpectreRunProgress(console);

        var ids = "0005-0000,0006-0000,0007-0000,0008-0000,0011-0000,0012-0000,0013-0000,0014-0000";
        progress.Begin(57);
        progress.StepBegin(ids);
        progress.StepEnd(Snapshot(10, 1), Snapshot(10, 1), TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(44));

        var dump = writer.ToString();
        Assert.Contains("…", dump, StringComparison.Ordinal);
        Assert.DoesNotContain(ids, dump, StringComparison.Ordinal);
    }

    private static (IAnsiConsole Console, StringWriter Writer) CreateConsole(bool interactive)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = interactive ? AnsiSupport.Yes : AnsiSupport.No,
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
            ColorSystem = ColorSystemSupport.Standard,
            Out = new AnsiConsoleOutput(writer)
        });
        return (console, writer);
    }

    private static LlmUsageSnapshot Snapshot(int promptTokens, int stepCount) => new()
    {
        PromptTokens = promptTokens,
        CachedTokens = 1,
        CompletionTokens = 2,
        Elapsed = TimeSpan.FromSeconds(stepCount),
        StepCount = stepCount,
        FailedCount = 0
    };

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
