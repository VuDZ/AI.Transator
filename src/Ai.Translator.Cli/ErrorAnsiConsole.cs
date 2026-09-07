using Spectre.Console;

namespace Ai.Translator.Cli;

internal static class ErrorAnsiConsole
{
    public static IAnsiConsole Create()
    {
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            Interactive = InteractionSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(Console.Error)
        });
    }
}
