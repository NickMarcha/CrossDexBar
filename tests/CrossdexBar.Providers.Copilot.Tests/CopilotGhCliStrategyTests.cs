using System.Net;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Providers.Copilot.Tests.Fakes;

namespace CrossdexBar.Providers.Copilot.Tests;

public class CopilotGhCliStrategyTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenGhAuthStatusFails()
    {
        var strategy = new CopilotGhCliStrategy();
        var context = CreateContext(
            new FakeCliRunner((_, args) => args.SequenceEqual(["auth", "status"]) ? new CliResult(1, "", "not logged in") : new CliResult(0, "", "")),
            new FakeHttpApi(_ => throw new InvalidOperationException()));

        var available = await strategy.IsAvailableAsync(context, CancellationToken.None);

        Assert.False(available);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenGhTokenCommandFails()
    {
        var strategy = new CopilotGhCliStrategy();
        var context = CreateContext(
            new FakeCliRunner((_, args) => args.SequenceEqual(["auth", "token"]) ? new CliResult(1, "", "no token") : new CliResult(0, "", "")),
            new FakeHttpApi(_ => throw new InvalidOperationException()));

        var outcome = await strategy.FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsSuccess_FromQuotaSnapshots()
    {
        var strategy = new CopilotGhCliStrategy();
        var context = CreateContext(
            new FakeCliRunner((_, _) => new CliResult(0, "gho_testtoken\n", "")),
            new FakeHttpApi(request =>
            {
                Assert.Equal("token", request.Headers.Authorization!.Scheme);
                Assert.Equal("gho_testtoken", request.Headers.Authorization.Parameter);

                const string body = """
                    {
                      "login": "nick",
                      "copilot_plan": "individual",
                      "quota_reset_date": "2026-08-01",
                      "quota_snapshots": {
                        "premium_interactions": { "has_quota": true, "unlimited": false, "percent_remaining": 63.7 },
                        "chat": { "has_quota": true, "unlimited": false, "percent_remaining": 75.0 },
                        "completions": { "has_quota": true, "unlimited": false, "percent_remaining": 50.0 }
                      }
                    }
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            }));

        var outcome = await strategy.FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal("Premium", success.Snapshot.Primary.Label);
        Assert.Equal(36.3, success.Snapshot.Primary.UsedPercent, 1);
        Assert.Equal("Chat", success.Snapshot.Secondary!.Label);
        Assert.Equal(25.0, success.Snapshot.Secondary.UsedPercent, 1);
        Assert.Equal("Completions", success.Snapshot.Tertiary!.Label);
        Assert.Equal(50.0, success.Snapshot.Tertiary.UsedPercent, 1);
    }

    [Fact]
    public async Task FetchAsync_FallsBackToLimitedUserQuotas_WhenSnapshotsMissing()
    {
        var strategy = new CopilotGhCliStrategy();
        var context = CreateContext(
            new FakeCliRunner((_, _) => new CliResult(0, "gho_testtoken\n", "")),
            new FakeHttpApi(_ =>
            {
                const string body = """
                    {
                      "quota_reset_date_utc": "08/01/2026 00:00:00",
                      "limited_user_quotas": { "premium_interactions": 40 },
                      "monthly_quotas": { "premium_interactions": 200 }
                    }
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            }));

        var outcome = await strategy.FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal("Premium", success.Snapshot.Primary.Label);
        Assert.Equal(80.0, success.Snapshot.Primary.UsedPercent, 1);
        Assert.Null(success.Snapshot.Secondary);
    }

    [Fact]
    public async Task FetchAsync_ReturnsUnavailable_WhenOnlyUnlimitedQuotasArePresent()
    {
        var strategy = new CopilotGhCliStrategy();
        var context = CreateContext(
            new FakeCliRunner((_, _) => new CliResult(0, "gho_testtoken\n", "")),
            new FakeHttpApi(_ =>
            {
                const string body = """
                    {
                      "quota_snapshots": {
                        "chat": { "has_quota": true, "unlimited": true, "percent_remaining": 100.0 }
                      }
                    }
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            }));

        var outcome = await strategy.FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.Unavailable>(outcome);
    }

    private static ProviderFetchContext CreateContext(ICliRunner cliRunner, IHttpApi httpApi)
    {
        var platformPaths = new FakePlatformPaths("C:\\test-home");
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "copilot", Label = "Copilot" };
        return new ProviderFetchContext
        {
            Instance = instance,
            CliRunner = cliRunner,
            HttpApi = httpApi,
            ConfigStore = new JsonConfigStore(platformPaths),
            PlatformPaths = platformPaths,
        };
    }
}
