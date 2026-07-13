using System.Net;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Providers.Claude.Tests.Fakes;

namespace CrossdexBar.Providers.Claude.Tests;

public class ClaudeAuthFileStrategyTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenNoCredentialsFileExists()
    {
        using var home = new TempDirectory();
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var available = await new ClaudeAuthFileStrategy().IsAvailableAsync(context, CancellationToken.None);

        Assert.False(available);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenNoCredentialsFileExists()
    {
        using var home = new TempDirectory();
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var outcome = await new ClaudeAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenCredentialsFileHasNoAccessToken()
    {
        using var home = new TempDirectory();
        WriteDefaultCredentialsFile(home.Path, """{ "claudeAiOauth": {} }""");
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var outcome = await new ClaudeAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsSuccess_UsingFiveHourAsPrimaryAndSevenDayAsSecondary()
    {
        using var home = new TempDirectory();
        WriteDefaultCredentialsFile(home.Path, """{ "claudeAiOauth": { "accessToken": "abc123" } }""");
        var http = new FakeHttpApi(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("abc123", request.Headers.Authorization!.Parameter);
            Assert.True(request.Headers.Contains("anthropic-beta"));
            const string body = """
                {
                  "five_hour": { "utilization": 33.0, "resets_at": "2026-07-12T18:00:00Z" },
                  "seven_day": { "utilization": 12.5, "resets_at": "2026-07-19T00:00:00Z" }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var context = CreateContext(home.Path, http);

        var outcome = await new ClaudeAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(33.0, success.Snapshot.Primary.UsedPercent);
        Assert.Equal("Session", success.Snapshot.Primary.Label);
        Assert.NotNull(success.Snapshot.Secondary);
        Assert.Equal(12.5, success.Snapshot.Secondary!.UsedPercent);
        Assert.Equal("Weekly", success.Snapshot.Secondary.Label);
        Assert.Null(success.Snapshot.Tertiary);
    }

    [Fact]
    public async Task FetchAsync_FallsBackToSevenDayAsPrimary_WhenFiveHourAbsent()
    {
        using var home = new TempDirectory();
        WriteDefaultCredentialsFile(home.Path, """{ "claudeAiOauth": { "accessToken": "abc123" } }""");
        var http = new FakeHttpApi(_ =>
        {
            const string body = """{ "seven_day": { "utilization": 8.0 } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var context = CreateContext(home.Path, http);

        var outcome = await new ClaudeAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(8.0, success.Snapshot.Primary.UsedPercent);
        Assert.Equal("Weekly", success.Snapshot.Primary.Label);
        Assert.Null(success.Snapshot.Secondary);
        Assert.Null(success.Snapshot.Tertiary);
    }

    [Fact]
    public async Task FetchAsync_UsesLimitsAndMapsWeeklyScopedToTertiaryMeter()
    {
        using var home = new TempDirectory();
        WriteDefaultCredentialsFile(home.Path, """{ "claudeAiOauth": { "accessToken": "abc123" } }""");
        var http = new FakeHttpApi(_ =>
        {
            const string body = """
                {
                  "limits": [
                    { "kind": "session", "percent": 66.0, "resets_at": "2026-07-13T04:19:59.945683+00:00" },
                    { "kind": "weekly_all", "percent": 65.0, "resets_at": "2026-07-15T05:59:59.945715+00:00" },
                    { "kind": "weekly_scoped", "percent": 8.0, "resets_at": "2026-07-15T05:59:59.946150+00:00" }
                  ]
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var context = CreateContext(home.Path, http);

        var outcome = await new ClaudeAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(66.0, success.Snapshot.Primary.UsedPercent);
        Assert.Equal("Session", success.Snapshot.Primary.Label);
        Assert.NotNull(success.Snapshot.Secondary);
        Assert.Equal(65.0, success.Snapshot.Secondary!.UsedPercent);
        Assert.Equal("Weekly", success.Snapshot.Secondary.Label);
        Assert.NotNull(success.Snapshot.Tertiary);
        Assert.Equal(8.0, success.Snapshot.Tertiary!.UsedPercent);
        Assert.Equal("Fable", success.Snapshot.Tertiary.Label);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenUsageApiReturnsUnauthorized()
    {
        using var home = new TempDirectory();
        WriteDefaultCredentialsFile(home.Path, """{ "claudeAiOauth": { "accessToken": "expired" } }""");
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var context = CreateContext(home.Path, http);

        var outcome = await new ClaudeAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsFailureWithRetryAfter_WhenUsageApiReturnsTooManyRequests()
    {
        using var home = new TempDirectory();
        WriteDefaultCredentialsFile(home.Path, """{ "claudeAiOauth": { "accessToken": "abc123" } }""");
        var http = new FakeHttpApi(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });
        var context = CreateContext(home.Path, http);

        var outcome = await new ClaudeAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        var failure = Assert.IsType<ProviderFetchOutcome.Failure>(outcome);
        Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
        Assert.NotNull(failure.RetryAfter);
        Assert.True(failure.RetryAfter > DateTimeOffset.UtcNow.AddSeconds(60));
    }

    [Fact]
    public async Task FetchAsync_HonorsCustomCredentialsFilePathSetting()
    {
        using var home = new TempDirectory();
        using var customDir = new TempDirectory();
        var customPath = Path.Combine(customDir.Path, "second-account.json");
        File.WriteAllText(customPath, """{ "claudeAiOauth": { "accessToken": "second" } }""");

        var http = new FakeHttpApi(request =>
        {
            Assert.Equal("second", request.Headers.Authorization!.Parameter);
            const string body = """{ "five_hour": { "utilization": 5.0 } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });

        var platformPaths = new FakePlatformPaths(home.Path);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "claude", Label = "second" };
        instance.Settings["credentialsFilePath"] = customPath;
        var context = new ProviderFetchContext
        {
            Instance = instance,
            CliRunner = new ProcessCliRunner(),
            HttpApi = http,
            ConfigStore = new JsonConfigStore(platformPaths),
            PlatformPaths = platformPaths,
        };

        var outcome = await new ClaudeAuthFileStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.Success>(outcome);
    }

    private static void WriteDefaultCredentialsFile(string homeDir, string json)
    {
        var claudeDir = Path.Combine(homeDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, ".credentials.json"), json);
    }

    private static ProviderFetchContext CreateContext(string homeDir, FakeHttpApi httpApi)
    {
        var platformPaths = new FakePlatformPaths(homeDir);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "claude", Label = "Claude" };
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
