using System.Net;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Providers.Ollama.Tests.Fakes;

namespace CrossdexBar.Providers.Ollama.Tests;

// OLLAMA_API_KEY (if set on the dev/CI machine) would otherwise leak into the "no key configured" test.
public class OllamaApiKeyStrategyTests : IDisposable
{
    private readonly string? _originalApiKey = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");

    public OllamaApiKeyStrategyTests() => Environment.SetEnvironmentVariable("OLLAMA_API_KEY", null);

    public void Dispose() => Environment.SetEnvironmentVariable("OLLAMA_API_KEY", _originalApiKey);

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenNoApiKeyConfigured()
    {
        var context = CreateContext(apiKey: null, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var outcome = await new OllamaApiKeyStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsUnavailable_WhenApiKeyIsValid()
    {
        var http = new FakeHttpApi(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("valid-key", request.Headers.Authorization!.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "models": [] }""") };
        });
        var context = CreateContext(apiKey: "valid-key", http);

        var outcome = await new OllamaApiKeyStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.Unavailable>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenApiKeyIsRejected()
    {
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var context = CreateContext(apiKey: "bad-key", http);

        var outcome = await new OllamaApiKeyStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task IsAvailableAsync_FallsBackToEnvironmentVariable_WhenNoInstanceSettingConfigured()
    {
        Environment.SetEnvironmentVariable("OLLAMA_API_KEY", "env-key");
        var context = CreateContext(apiKey: null, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var available = await new OllamaApiKeyStrategy().IsAvailableAsync(context, CancellationToken.None);

        Assert.True(available);
    }

    private static ProviderFetchContext CreateContext(string? apiKey, FakeHttpApi httpApi)
    {
        var platformPaths = new FakePlatformPaths(Path.GetTempPath());
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "ollama", Label = "Ollama" };
        if (apiKey is not null)
            instance.Settings["apiKey"] = apiKey;

        return new ProviderFetchContext
        {
            Instance = instance,
            CliRunner = new ProcessCliRunner(),
            HttpApi = httpApi,
            ConfigStore = new JsonConfigStore(platformPaths),
            PlatformPaths = platformPaths,
        };
    }
}
