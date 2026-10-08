using System.Net;
using System.Text;
using Ai.Translator.Core;
using Ai.Translator.Core.Progress;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ai.Translator.Tests;

public sealed class HttpClientLoggingTests
{
    [Theory]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Trace)]
    public async Task LlmHttpClient_VerboseLogging_DoesNotLogRequests(LogLevel minimumLevel)
    {
        var collector = new CollectingLoggerProvider();
        const string json =
            """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "System.Net.Http": "Warning",
                  "System.Net.Http.HttpClient": "Warning",
                  "System.Net.Http.HttpClient.llm": "Warning"
                }
              },
              "Llm": {
                "BaseUrl": "https://gateway.test/v1",
                "Model": "test-model",
                "ApiKey": "test-key",
                "TimeoutSeconds": 30
              }
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = minimumLevel.ToString(),
                ["Logging:LogLevel:System.Net.Http"] = minimumLevel.ToString(),
                ["Logging:LogLevel:System.Net.Http.HttpClient"] = minimumLevel.ToString(),
                ["Logging:LogLevel:System.Net.Http.HttpClient.llm"] = minimumLevel.ToString(),
                ["Logging:LogLevel:System.Net.Http.HttpClient.llm.LogicalHandler"] = minimumLevel.ToString(),
                ["Logging:LogLevel:System.Net.Http.HttpClient.llm.ClientHandler"] = minimumLevel.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(minimumLevel);
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddProvider(collector);
        });
        services.AddTranslator(configuration);
        services.AddHttpClient(ServiceCollectionExtensions.LlmHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler());

        services.AddHttpClient("other")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler());

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ILoggerFactory>();
        var applicationLogger = factory.CreateLogger("Ai.Translator.Tests.Probe");
        applicationLogger.LogInformation("probe-visible");
        applicationLogger.LogWarning("retry-visible");
        applicationLogger.LogError("failure-visible");

        using var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionExtensions.LlmHttpClientName);
        using var response = await client.PostAsync(
            "chat/completions",
            new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(collector.Entries, e => e.Contains("probe-visible", StringComparison.Ordinal));
        Assert.Contains(collector.Entries, e => e.Contains("retry-visible", StringComparison.Ordinal));
        Assert.Contains(collector.Entries, e => e.Contains("failure-visible", StringComparison.Ordinal));
        Assert.DoesNotContain(collector.Entries, e => e.StartsWith("System.Net.Http.HttpClient.llm.", StringComparison.Ordinal));

        using var otherClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("other");
        using var otherResponse = await otherClient.GetAsync("https://gateway.test/health");
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        Assert.Contains(collector.Entries, e => e.StartsWith("System.Net.Http.HttpClient.other.", StringComparison.Ordinal)
            && e.Contains("Sending HTTP request", StringComparison.Ordinal));
    }

    [Fact]
    public void Appsettings_SetsHttpClientCategoriesToWarning()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Ai.Translator.Cli", "appsettings.json");
        var text = File.ReadAllText(path);
        Assert.Contains("\"System.Net.Http\": \"Warning\"", text, StringComparison.Ordinal);
        Assert.Contains("\"System.Net.Http.HttpClient\": \"Warning\"", text, StringComparison.Ordinal);
        Assert.Contains("\"System.Net.Http.HttpClient.llm\": \"Warning\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreAssembly_DoesNotReferenceSpectreConsole()
    {
        var names = typeof(NullRunProgress).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(names, name => string.Equals(name.Name, "Spectre.Console", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ai.Translator.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly string _category;
        private readonly List<string> _entries;

        public CollectingLogger(string category, List<string> entries)
        {
            _category = category;
            _entries = entries;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(_category + ": " + formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
