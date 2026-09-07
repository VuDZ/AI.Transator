using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Spectre.Console;

namespace Ai.Translator.Cli;

internal sealed class SpectreRunProgress : IRunProgress
{
    private const int BarWidth = 24;
    private readonly IAnsiConsole _console;
    private int _total;
    private int _done;
    private string _label = string.Empty;

    public SpectreRunProgress(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
    }

    public void Begin(int totalSteps)
    {
        _total = totalSteps;
        _done = 0;
        _label = string.Empty;
        if (totalSteps <= 0)
        {
            return;
        }

        _console.MarkupLine($"[grey]0/{totalSteps}[/]");
    }

    public void StepBegin(string label)
    {
        _label = label ?? string.Empty;
        _console.MarkupLine($"[blue]…[/] {Markup.Escape(_label)}");
    }

    public void StepEnd(LlmUsageSnapshot last, LlmUsageSnapshot totals, TimeSpan etaOrZero)
    {
        ArgumentNullException.ThrowIfNull(last);
        ArgumentNullException.ThrowIfNull(totals);
        _done++;
        var eta = etaOrZero <= TimeSpan.Zero ? "—" : FormatDuration(etaOrZero);
        _console.MarkupLine(
            $"{RenderBar(_done, _total)} [green]{_done}[/]/[grey]{_total}[/] {Markup.Escape(_label)}  ETA {Markup.Escape(eta)}");
        WriteUsage(last, totals);
    }

    public void Complete(LlmUsageSnapshot totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        var table = CreateUsageTable();
        table.AddRow(
            "[bold]total[/]",
            FormatDuration(totals.Elapsed),
            totals.PromptTokens.ToString(),
            FormatNullable(totals.CachedTokens),
            FormatNullable(totals.CompletionTokens),
            totals.StepCount.ToString(),
            totals.FailedCount.ToString());
        _console.Write(table);
    }

    private void WriteUsage(LlmUsageSnapshot last, LlmUsageSnapshot totals)
    {
        var table = CreateUsageTable();
        table.AddRow(
            "last",
            FormatDuration(last.Elapsed),
            last.PromptTokens.ToString(),
            FormatNullable(last.CachedTokens),
            FormatNullable(last.CompletionTokens),
            last.StepCount.ToString(),
            last.FailedCount.ToString());
        table.AddRow(
            "total",
            FormatDuration(totals.Elapsed),
            totals.PromptTokens.ToString(),
            FormatNullable(totals.CachedTokens),
            FormatNullable(totals.CompletionTokens),
            totals.StepCount.ToString(),
            totals.FailedCount.ToString());
        _console.Write(table);
    }

    private static Table CreateUsageTable()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(string.Empty)
            .AddColumn("elapsed")
            .AddColumn("prompt")
            .AddColumn("cached")
            .AddColumn("completion")
            .AddColumn("steps")
            .AddColumn("failed");
        return table;
    }

    private static string RenderBar(int done, int total)
    {
        if (total <= 0)
        {
            return $"[grey]{new string('░', BarWidth)}[/]";
        }

        var filled = (int)Math.Round(BarWidth * (double)done / total);
        filled = Math.Clamp(filled, 0, BarWidth);
        var bar = new string('█', filled) + new string('░', BarWidth - filled);
        return $"[green]{bar}[/]";
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "0s";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}h {value.Minutes:D2}m";
        }

        if (value.TotalMinutes >= 1)
        {
            return $"{(int)value.TotalMinutes}m {value.Seconds:D2}s";
        }

        return $"{value.TotalSeconds:0.0}s";
    }

    private static string FormatNullable(int? value) => value is null ? "—" : value.Value.ToString();
}
