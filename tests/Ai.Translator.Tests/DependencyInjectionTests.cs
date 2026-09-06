using System.Text;
using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ai.Translator.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddTranslator_BindsOptionsFromJsonAndBuildsContainer()
    {
        const string json =
            """
            {
              "Translator": {
                "TargetLanguage": "ru",
                "MaxRetries": 2,
                "Temperature": 0.1
              },
              "Llm": {
                "BaseUrl": "http://localhost:11434/v1",
                "Model": "test-model",
                "ApiKey": "test-key",
                "ContextWindowTokens": 8192,
                "ReservedOutputTokens": 512,
                "CacheMode": "none"
              }
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var services = new ServiceCollection();
        services.AddTranslator(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var translator = provider.GetRequiredService<IOptions<TranslatorOptions>>().Value;
        Assert.Equal("ru", translator.TargetLanguage);
        Assert.Equal(2, translator.MaxRetries);
        Assert.Equal(0.1, translator.Temperature);

        var llm = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
        Assert.Equal("http://localhost:11434/v1", llm.BaseUrl);
        Assert.Equal("test-model", llm.Model);
        Assert.Equal("test-key", llm.ApiKey);

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(ServiceCollectionExtensions.LlmHttpClientName);
        Assert.Equal(new Uri("http://localhost:11434/v1/"), client.BaseAddress);

        Assert.NotNull(provider.GetRequiredService<IGlossaryParser>());
        Assert.NotNull(provider.GetRequiredService<IGlossaryWriter>());
        Assert.NotNull(provider.GetRequiredService<IGlossaryCompiler>());
        Assert.NotNull(provider.GetRequiredService<IEpubBookService>());
        Assert.NotNull(provider.GetRequiredService<IGlossaryCompileService>());
        Assert.NotNull(provider.GetRequiredService<ITokenEstimator>());
        Assert.NotNull(provider.GetRequiredService<ITranslationPromptFactory>());
        Assert.NotNull(provider.GetRequiredService<IChapterChunker>());
        Assert.NotNull(provider.GetRequiredService<ITranslationValidator>());
        Assert.NotNull(provider.GetRequiredService<ICheckpointStore>());
        Assert.NotNull(provider.GetRequiredService<ILlmProvider>());
        Assert.NotNull(provider.GetRequiredService<IBookTranslationService>());
        Assert.NotNull(provider.GetRequiredService<TimeProvider>());
    }
}
