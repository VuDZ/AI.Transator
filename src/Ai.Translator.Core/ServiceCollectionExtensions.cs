using System.Net.Http.Headers;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Epub;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ai.Translator.Core;

public static class ServiceCollectionExtensions
{
    public const string LlmHttpClientName = "llm";

    public static IServiceCollection AddTranslator(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TranslatorOptions>()
            .Bind(configuration.GetSection(TranslatorOptions.SectionName));
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName));

        services.AddHttpClient(LlmHttpClientName, (provider, client) =>
        {
            var llm = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(llm.BaseUrl))
            {
                var baseUrl = llm.BaseUrl.EndsWith('/') ? llm.BaseUrl : llm.BaseUrl + "/";
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            }

            if (!string.IsNullOrWhiteSpace(llm.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", llm.ApiKey);
            }
        });

        services.AddSingleton<IGlossaryParser, GlossaryParser>();
        services.AddSingleton<IGlossaryWriter, GlossaryWriter>();
        services.AddSingleton<IGlossaryCompiler, GlossaryCompiler>();
        services.AddSingleton<IBookTextExtractor, EpubBookTextExtractor>();
        services.AddSingleton<IGlossaryCompileService, GlossaryCompileService>();

        return services;
    }
}
