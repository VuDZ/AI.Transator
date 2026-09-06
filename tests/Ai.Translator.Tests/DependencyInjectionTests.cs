using System.Text;
using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Llm;
using Ai.Translator.Core.Options;
using Ai.Translator.Core.Translation;
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
                "TimeoutSeconds": 180,
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
        Assert.Equal(180, llm.TimeoutSeconds);

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(ServiceCollectionExtensions.LlmHttpClientName);
        Assert.Equal(new Uri("http://localhost:11434/v1/"), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(180), client.Timeout);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal("test-key", client.DefaultRequestHeaders.Authorization?.Parameter);

        Assert.NotNull(provider.GetRequiredService<IGlossaryParser>());
        Assert.NotNull(provider.GetRequiredService<IGlossaryWriter>());
        Assert.NotNull(provider.GetRequiredService<IGlossaryCompiler>());
        Assert.NotNull(provider.GetRequiredService<IGlossaryMerger>());
        Assert.NotNull(provider.GetRequiredService<IEpubBookService>());
        Assert.NotNull(provider.GetRequiredService<IGlossaryCompileService>());
        Assert.NotNull(provider.GetRequiredService<ITokenEstimator>());
        Assert.NotNull(provider.GetRequiredService<ITranslationPromptFactory>());
        Assert.NotNull(provider.GetRequiredService<StyleRulesLoader>());
        Assert.NotNull(provider.GetRequiredService<ExtractRulesLoader>());
        Assert.NotNull(provider.GetRequiredService<IChapterChunker>());
        Assert.NotNull(provider.GetRequiredService<ITranslationValidator>());
        Assert.NotNull(provider.GetRequiredService<ICheckpointStore>());
        Assert.IsType<ChatCompletionsLlmProvider>(provider.GetRequiredService<ILlmProvider>());
        Assert.NotNull(provider.GetRequiredService<IBookTranslationService>());
        Assert.IsType<GlossaryExtractor>(provider.GetRequiredService<IGlossaryExtractor>());
        Assert.NotNull(provider.GetRequiredService<IGlossaryExtractService>());
        Assert.NotNull(provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddTranslator_DifferentBaseUrl_ChangesHttpClientAddressNotPipeline()
    {
        const string json =
            """
            {
              "Llm": {
                "BaseUrl": "https://openrouter.ai/api/v1",
                "Model": "other-model",
                "ApiKey": "other-key"
              }
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var services = new ServiceCollection();
        services.AddTranslator(configuration);
        using var provider = services.BuildServiceProvider();

        var llm = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
        Assert.Equal("https://openrouter.ai/api/v1", llm.BaseUrl);
        Assert.Equal(300, llm.TimeoutSeconds);

        using var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionExtensions.LlmHttpClientName);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/"), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(300), client.Timeout);
        Assert.IsType<ChatCompletionsLlmProvider>(provider.GetRequiredService<ILlmProvider>());
        Assert.IsType<BookTranslationService>(provider.GetRequiredService<IBookTranslationService>());
    }
}
