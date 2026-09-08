using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ai.Translator.Cli;

internal sealed class SpectreRunProgress : IRunProgress, IDisposable
{
    private const int BarWidth = 24;
    private readonly IAnsiConsole _console;
    private int _total;
    private int _done;
    private string _label = string.Empty;
    private TimeSpan _eta;
    private LlmUsageSnapshot? _last;
    private LlmUsageSnapshot? _totals;
    private int _liveHeight;
    private bool _liveActive;
    private bool _disposed;

    public SpectreRunProgress(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
    }

    private bool UseLive =>
        !_disposed
        && _console.Profile.Capabilities.Interactive
        && _console.Profile.Capabilities.Ansi;

    public void Begin(int totalSteps)
    {
        _total = totalSteps;
        _done = 0;
        _label = string.Empty;
        _eta = TimeSpan.Zero;
        _last = null;
        _totals = null;
        if (totalSteps <= 0)
        {
            return;
        }

        if (UseLive)
        {
            RenderLive();
        }
    }

    public void StepBegin(string label)
    {
        _label = label ?? string.Empty;
        if (UseLive)
        {
            RenderLive();
        }
    }

    public void StepEnd(LlmUsageSnapshot last, LlmUsageSnapshot totals, TimeSpan etaOrZero)
    {
        ArgumentNullException.ThrowIfNull(last);
        ArgumentNullException.ThrowIfNull(totals);
        _done++;
        _last = last;
        _totals = totals;
        _eta = etaOrZero;
        if (UseLive)
        {
            RenderLive();
            return;
        }

        _console.MarkupLine(BuildBarMarkup());
    }

    public void Complete(LlmUsageSnapshot totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        _totals = totals;
        if (_liveActive)
        {
            RenderLive();
            StopLive();
            return;
        }

        WriteTotalsTable(totals);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopLive();
    }

    private void RenderLive()
    {
        var block = BuildLiveBlock();
        if (_liveActive && _liveHeight > 0)
        {
            var moveUp = Math.Max(0, _liveHeight - 1);
            _console.WriteAnsi(writer =>
            {
                writer.HideCursor();
                writer.Write("\r");
                if (moveUp > 0)
                {
                    writer.CursorUp(moveUp);
                }

                writer.EraseInDisplay(0);
            });
        }
        else
        {
            _console.Cursor.Hide();
        }

        _console.Write(block);
        _liveHeight = MeasureHeight(block);
        _liveActive = true;
    }

    private void StopLive()
    {
        if (!_liveActive)
        {
            return;
        }

        _console.Cursor.Show();
        _console.WriteLine();
        _liveActive = false;
        _liveHeight = 0;
    }

    private IRenderable BuildLiveBlock()
    {
        var bar = new Markup(BuildBarMarkup());
        if (_last is null || _totals is null)
        {
            return bar;
        }

        return new Rows(bar, CreateLastTotalTable(_last, _totals));
    }

    private string BuildBarMarkup()
    {
        var eta = _eta <= TimeSpan.Zero ? "—" : FormatDuration(_eta);
        var label = string.IsNullOrEmpty(_label) ? string.Empty : " " + Markup.Escape(_label);
        return $"{RenderBar(_done, _total)} [green]{_done}[/]/[grey]{_total}[/]{label}  ETA {Markup.Escape(eta)}";
    }

    private void WriteTotalsTable(LlmUsageSnapshot totals)
    {
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

    private static Table CreateLastTotalTable(LlmUsageSnapshot last, LlmUsageSnapshot totals)
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
        return table;
    }

    private static Table CreateUsageTable()
    {
        return new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(string.Empty)
            .AddColumn("elapsed")
            .AddColumn("prompt")
            .AddColumn("cached")
            .AddColumn("completion")
            .AddColumn("steps")
            .AddColumn("failed");
    }

    private int MeasureHeight(IRenderable renderable)
    {
        var lines = Segment.SplitLines(renderable.GetSegments(_console));
        return Math.Max(1, lines.Count);
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
