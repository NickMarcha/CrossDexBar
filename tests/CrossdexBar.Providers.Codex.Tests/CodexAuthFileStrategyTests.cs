using System.Net;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Providers.Codex.Tests.Fakes;

namespace CrossdexBar.Providers.Codex.Tests;

// CODEX_HOME (if set on the dev/CI machine) would otherwise override the fake home directory these tests use.
public class CodexAuthFileStrategyTests : IDisposable
{
    private readonly string? _originalCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");

    public CodexAuthFileStrategyTests() => Environment.SetEnvironmentVariable("CODEX_HOME", null);

    public void Dispose() => Environment.SetEnvironmentVariable("CODEX_HOME", _originalCodexHome);

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenNoAuthFileExists()
    {
        using var home = new TempDirectory();
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var available = await new CodexAuthFileStrategy().IsAvailableAsync(context, CancellationToken.None);

        Assert.False(available);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenNoAuthFileExists()
    {
        using var home = new TempDirectory();
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var outcome = await new CodexAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenAuthFileHasNoAccessToken()
    {
        using var home = new TempDirectory();
        WriteDefaultAuthFile(home.Path, """{ "tokens": {} }""");
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var outcome = await new CodexAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsSuccess_MappingUsageResponse()
    {
        using var home = new TempDirectory();
        WriteDefaultAuthFile(home.Path, """{ "tokens": { "access_token": "abc123" } }""");
        var http = new FakeHttpApi(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("abc123", request.Headers.Authorization!.Parameter);
            const string body = """
                {
                  "rate_limit": {
                    "primary_window": { "used_percent": 42.5, "reset_at": 1799999999 },
                    "secondary_window": { "used_percent": 10, "reset_at": 1800999999 }
                  }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var context = CreateContext(home.Path, http);

        var outcome = await new CodexAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(42.5, success.Snapshot.Primary.UsedPercent);
        Assert.Equal("Session", success.Snapshot.Primary.Label);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1799999999), success.Snapshot.Primary.ResetsAt);
        Assert.NotNull(success.Snapshot.Secondary);
        Assert.Equal(10, success.Snapshot.Secondary!.UsedPercent);
        Assert.Equal("Weekly", success.Snapshot.Secondary.Label);
    }

    [Fact]
    public async Task FetchAsync_PromotesSecondaryToPrimary_WhenPrimaryWindowAbsent()
    {
        using var home = new TempDirectory();
        WriteDefaultAuthFile(home.Path, """{ "tokens": { "access_token": "abc123" } }""");
        var http = new FakeHttpApi(_ =>
        {
            const string body = """{ "rate_limit": { "secondary_window": { "used_percent": 7 } } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var context = CreateContext(home.Path, http);

        var outcome = await new CodexAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(7, success.Snapshot.Primary.UsedPercent);
        Assert.Equal("Weekly", success.Snapshot.Primary.Label);
        Assert.Null(success.Snapshot.Secondary);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenUsageApiReturnsUnauthorized()
    {
        using var home = new TempDirectory();
        WriteDefaultAuthFile(home.Path, """{ "tokens": { "access_token": "expired" } }""");
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var context = CreateContext(home.Path, http);

        var outcome = await new CodexAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsFailure_WhenUsageApiReturnsServerError()
    {
        using var home = new TempDirectory();
        WriteDefaultAuthFile(home.Path, """{ "tokens": { "access_token": "abc123" } }""");
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var context = CreateContext(home.Path, http);

        var outcome = await new CodexAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.Failure>(outcome);
    }

    [Fact]
    public async Task FetchAsync_HonorsCustomAuthFilePathSetting()
    {
        using var home = new TempDirectory();
        using var customDir = new TempDirectory();
        var customPath = Path.Combine(customDir.Path, "second-account.json");
        File.WriteAllText(customPath, """{ "tokens": { "access_token": "second" } }""");

        var http = new FakeHttpApi(request =>
        {
            Assert.Equal("second", request.Headers.Authorization!.Parameter);
            const string body = """{ "rate_limit": { "primary_window": { "used_percent": 5 } } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });

        var platformPaths = new FakePlatformPaths(home.Path);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "codex", Label = "second" };
        instance.Settings["authFilePath"] = customPath;
        var context = new ProviderFetchContext
        {
            Instance = instance,
            CliRunner = new ProcessCliRunner(),
            HttpApi = http,
            ConfigStore = new JsonConfigStore(platformPaths),
            PlatformPaths = platformPaths,
        };

        var outcome = await new CodexAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.Success>(outcome);
    }

    private static void WriteDefaultAuthFile(string homeDir, string json)
    {
        var codexDir = Path.Combine(homeDir, ".codex");
        Directory.CreateDirectory(codexDir);
        File.WriteAllText(Path.Combine(codexDir, "auth.json"), json);
    }

    private static ProviderFetchContext CreateContext(string homeDir, FakeHttpApi httpApi)
    {
        var platformPaths = new FakePlatformPaths(homeDir);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "codex", Label = "Codex" };
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
