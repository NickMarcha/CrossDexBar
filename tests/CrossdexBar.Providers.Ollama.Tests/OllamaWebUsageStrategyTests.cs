using System.Net;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Providers.Ollama.Tests.Fakes;

namespace CrossdexBar.Providers.Ollama.Tests;

public class OllamaWebUsageStrategyTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenNoCookieConfigured()
    {
        var context = CreateContext(cookieHeader: null, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var available = await new OllamaWebUsageStrategy().IsAvailableAsync(context, CancellationToken.None);

        Assert.False(available);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenNoCookieConfigured()
    {
        var context = CreateContext(cookieHeader: null, new FakeHttpApi(_ => throw new InvalidOperationException()));

        var outcome = await new OllamaWebUsageStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ParsesSessionAndWeeklyUsage_FromUsedPercentPattern()
    {
        const string html = """
            <div>Session usage</div><div class="bar">42% used</div><span>resets at 2026-08-01T00:00:00Z</span>
            <div>Weekly usage</div><div class="bar">17% used</div>
            """;
        var http = new FakeHttpApi(request =>
        {
            var cookie = Assert.Single(request.Headers.GetValues("Cookie"));
            Assert.Equal("session=abc123", cookie);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
        });
        var context = CreateContext(cookieHeader: "session=abc123", http);

        var outcome = await new OllamaWebUsageStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(42, success.Snapshot.Primary.UsedPercent);
        Assert.Equal("Session", success.Snapshot.Primary.Label);
        Assert.NotNull(success.Snapshot.Primary.ResetsAt);
        Assert.NotNull(success.Snapshot.Secondary);
        Assert.Equal(17, success.Snapshot.Secondary!.UsedPercent);
        Assert.Equal("Weekly", success.Snapshot.Secondary.Label);
    }

    [Fact]
    public async Task FetchAsync_FallsBackToProgressBarWidth_WhenNoUsedPercentText()
    {
        const string html = """<div>Session usage</div><div class="bar" style="width: 30%;"></div>""";
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        var context = CreateContext(cookieHeader: "session=abc123", http);

        var outcome = await new OllamaWebUsageStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(30, success.Snapshot.Primary.UsedPercent);
    }

    [Fact]
    public async Task FetchAsync_PromotesWeeklyToPrimary_WhenSessionUsageAbsent()
    {
        const string html = """<div>Weekly usage</div><div class="bar">55% used</div>""";
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        var context = CreateContext(cookieHeader: "session=abc123", http);

        var outcome = await new OllamaWebUsageStrategy().FetchAsync(context, CancellationToken.None);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(55, success.Snapshot.Primary.UsedPercent);
        Assert.Equal("Weekly", success.Snapshot.Primary.Label);
        Assert.Null(success.Snapshot.Secondary);
    }

    [Fact]
    public async Task FetchAsync_ReturnsFailure_WhenNoUsageDataFound()
    {
        const string html = "<html><body>Sign in to see your usage</body></html>";
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        var context = CreateContext(cookieHeader: "session=abc123", http);

        var outcome = await new OllamaWebUsageStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.Failure>(outcome);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNotSignedIn_WhenCookieIsRejected()
    {
        var http = new FakeHttpApi(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var context = CreateContext(cookieHeader: "session=expired", http);

        var outcome = await new OllamaWebUsageStrategy().FetchAsync(context, CancellationToken.None);

        Assert.IsType<ProviderFetchOutcome.NotSignedIn>(outcome);
    }

    private static ProviderFetchContext CreateContext(string? cookieHeader, FakeHttpApi httpApi)
    {
        var platformPaths = new FakePlatformPaths(Path.GetTempPath());
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "ollama", Label = "Ollama" };
        if (cookieHeader is not null)
            instance.Settings["cookieHeader"] = cookieHeader;

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
