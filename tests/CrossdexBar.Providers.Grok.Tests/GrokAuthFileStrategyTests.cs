using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Providers.Grok.Tests.Fakes;

namespace CrossdexBar.Providers.Grok.Tests;

// GROK_HOME (if set on the dev/CI machine) would otherwise override the fake home directory these tests use.
public class GrokAuthFileStrategyTests : IDisposable
{
    private readonly string? _originalGrokHome = Environment.GetEnvironmentVariable("GROK_HOME");

    public GrokAuthFileStrategyTests() => Environment.SetEnvironmentVariable("GROK_HOME", null);

    public void Dispose() => Environment.SetEnvironmentVariable("GROK_HOME", _originalGrokHome);

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenNoAuthFileExists()
    {
        using var home = new TempDirectory();
        var context = CreateContext(home.Path);

        var outcome = await new GrokAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsUnavailable_WithEmail_WhenOidcScopeEntryPresent()
    {
        using var home = new TempDirectory();
        WriteAuthFile(home.Path, """
            {
              "https://auth.x.ai::client123": { "key": "abc", "email": "user@example.com", "auth_mode": "oidc" }
            }
            """);
        var context = CreateContext(home.Path);

        var outcome = await new GrokAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        var unavailable = Assert.IsType<ProviderFetchOutcome.Unavailable>(outcome);
        Assert.Contains("user@example.com", unavailable.Message);
    }

    [Fact]
    public async Task FetchAsync_PrefersOidcScope_OverLegacySessionScope()
    {
        using var home = new TempDirectory();
        WriteAuthFile(home.Path, """
            {
              "https://accounts.x.ai/sign-in": { "key": "legacy-key", "email": "legacy@example.com" },
              "https://auth.x.ai::client123": { "key": "oidc-key", "email": "oidc@example.com" }
            }
            """);
        var context = CreateContext(home.Path);

        var outcome = await new GrokAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        var unavailable = Assert.IsType<ProviderFetchOutcome.Unavailable>(outcome);
        Assert.Contains("oidc@example.com", unavailable.Message);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenEntriesHaveNoUsableKey()
    {
        using var home = new TempDirectory();
        WriteAuthFile(home.Path, """{ "https://auth.x.ai::client123": { "key": "", "email": "user@example.com" } }""");
        var context = CreateContext(home.Path);

        var outcome = await new GrokAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    private static void WriteAuthFile(string homeDir, string json)
    {
        var grokDir = Path.Combine(homeDir, ".grok");
        Directory.CreateDirectory(grokDir);
        File.WriteAllText(Path.Combine(grokDir, "auth.json"), json);
    }

    private static ProviderFetchContext CreateContext(string homeDir)
    {
        var platformPaths = new FakePlatformPaths(homeDir);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "grok", Label = "Grok" };
        return new ProviderFetchContext
        {
            Instance = instance,
            CliRunner = new ProcessCliRunner(),
            HttpApi = new FakeHttpApi(_ => throw new InvalidOperationException("Grok's auth-file strategy should not make network calls")),
            ConfigStore = new JsonConfigStore(platformPaths),
            PlatformPaths = platformPaths,
        };
    }
}
