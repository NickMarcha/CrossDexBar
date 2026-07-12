using System.Net;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Providers.Cursor.Tests.Fakes;

namespace CrossdexBar.Providers.Cursor.Tests;

public class CursorAppAuthStrategyTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenNoVscdbExists()
    {
        using var home = new TempDirectory();
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var available = await new CursorAppAuthStrategy().IsAvailableAsync(context, CancellationToken.None);

        Assert.False(available);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenNoVscdbExists()
    {
        using var home = new TempDirectory();
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var outcome = await new CursorAppAuthStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenTokenIsAlreadyExpired()
    {
        using var home = new TempDirectory();
        var vscdbPath = WriteDefaultVscdb(home.Path, TestJwt.Create("auth0|user123", expiresAtUnixSeconds: 1000));
        var context = CreateContext(home.Path, new FakeHttpApi(_ => throw new InvalidOperationException("should not call network for an expired token")));

        var outcome = await new CursorAppAuthStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
        Assert.True(File.Exists(vscdbPath));
    }

    [Fact]
    public async Task FetchAsync_ReturnsSuccess_MappingUsageSummaryResponse()
    {
        using var home = new TempDirectory();
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        WriteDefaultVscdb(home.Path, TestJwt.Create("auth0|user123", futureExpiry));

        var http = new FakeHttpApi(request =>
        {
            var cookie = Assert.Single(request.Headers.GetValues("Cookie"));
            Assert.StartsWith("WorkosCursorSessionToken=user123%3A%3A", cookie);
            const string body = """
                {
                  "billingCycleEnd": "2026-08-01T00:00:00Z",
                  "individualUsage": { "plan": { "used": 25, "limit": 100, "totalPercentUsed": 25.0 } }
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var context = CreateContext(home.Path, http);

        var outcome = await new CursorAppAuthStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(25.0, success.Snapshot.Primary.UsedPercent);
        Assert.Equal("Plan", success.Snapshot.Primary.Label);
        Assert.NotNull(success.Snapshot.Primary.ResetsAt);
        Assert.Null(success.Snapshot.Secondary);
    }

    [Fact]
    public async Task FetchAsync_ComputesPercentFromUsedAndLimit_WhenTotalPercentUsedMissing()
    {
        using var home = new TempDirectory();
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        WriteDefaultVscdb(home.Path, TestJwt.Create("auth0|user123", futureExpiry));

        var http = new FakeHttpApi(_ =>
        {
            const string body = """{ "individualUsage": { "plan": { "used": 40, "limit": 200 } } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var context = CreateContext(home.Path, http);

        var outcome = await new CursorAppAuthStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(20.0, success.Snapshot.Primary.UsedPercent);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenUsageApiReturnsUnauthorized()
    {
        using var home = new TempDirectory();
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        WriteDefaultVscdb(home.Path, TestJwt.Create("auth0|user123", futureExpiry));
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var context = CreateContext(home.Path, http);

        var outcome = await new CursorAppAuthStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_HonorsCustomVscdbPathSetting()
    {
        using var home = new TempDirectory();
        using var customDir = new TempDirectory();
        var customPath = Path.Combine(customDir.Path, "second-account.vscdb");
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        TestVscdb.CreateWithAccessToken(customPath, TestJwt.Create("auth0|second-user", futureExpiry));

        var http = new FakeHttpApi(request =>
        {
            var cookie = Assert.Single(request.Headers.GetValues("Cookie"));
            Assert.StartsWith("WorkosCursorSessionToken=second-user%3A%3A", cookie);
            const string body = """{ "individualUsage": { "plan": { "totalPercentUsed": 5.0 } } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });

        var platformPaths = new FakePlatformPaths(home.Path);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "cursor", Label = "second" };
        instance.Settings["vscdbPath"] = customPath;
        var context = new ProviderFetchContext
        {
            Instance = instance,
            CliRunner = new ProcessCliRunner(),
            HttpApi = http,
            ConfigStore = new JsonConfigStore(platformPaths),
            PlatformPaths = platformPaths,
        };

        var outcome = await new CursorAppAuthStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.Success>(outcome);
    }

    private static string WriteDefaultVscdb(string homeDir, string accessToken)
    {
        var vscdbPath = Path.Combine(homeDir, "appdata", "Cursor", "User", "globalStorage", "state.vscdb");
        Directory.CreateDirectory(Path.GetDirectoryName(vscdbPath)!);
        TestVscdb.CreateWithAccessToken(vscdbPath, accessToken);
        return vscdbPath;
    }

    private static ProviderFetchContext CreateContext(string homeDir, FakeHttpApi httpApi)
    {
        var platformPaths = new FakePlatformPaths(homeDir);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "cursor", Label = "Cursor" };
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
