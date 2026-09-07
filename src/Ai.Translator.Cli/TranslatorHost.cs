using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ai.Translator.Cli;

internal static class TranslatorHost
{
    public static HostApplicationBuilder CreateBuilder()
    {
        var settings = new HostApplicationBuilderSettings
        {
            Args = [],
            ApplicationName = "ai-translator",
            ContentRootPath = AppContext.BaseDirectory
        };

        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddConsole();
        builder.Services.AddTranslator(builder.Configuration);
        builder.Services.AddSingleton<IRunProgress>(_ => new SpectreRunProgress(ErrorAnsiConsole.Create()));
        return builder;
    }
}
