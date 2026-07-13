using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Core.Refresh;
using CrossdexBar.Core.Tests.Fakes;

namespace CrossdexBar.Core.Tests;

public class RefreshServiceTests
{
    private static RefreshService CreateService(
        ProviderRegistry registry,
        TimeSpan? minRefreshInterval = null,
        TimeSpan? defaultRateLimitBackoff = null,
        Func<DateTimeOffset>? now = null) =>
        new(registry, new ProcessCliRunner(), new HttpApi(), new FakeConfigStore(), new PlatformPaths(),
            minRefreshInterval, defaultRateLimitBackoff, now);

    private static ProviderDescriptor MakeDescriptor(string id, params IFetchStrategy[] strategies) => new()
    {
        Id = id,
        DisplayName = id,
        Branding = new ProviderBranding("#000000", "icon"),
        Strategies = strategies,
    };

    [Fact]
    public async Task RefreshInstanceAsync_ReturnsFailure_ForUnknownProvider()
    {
        var service = CreateService(new ProviderRegistry());
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "unknown", Label = "x" };

        var outcome = await service.RefreshInstanceAsync(instance);

        Assert.IsType<ProviderFetchOutcome.Failure>(outcome);
    }

    [Fact]
    public async Task RefreshInstanceAsync_ReturnsSuccess_WhenStrategySucceeds()
    {
        var snapshot = new UsageSnapshot(new UsageWindow(50, null, "Session"), null, null, DateTimeOffset.UtcNow, "fake");
        var strategy = new FakeFetchStrategy((_, _) => Task.FromResult<ProviderFetchOutcome>(new ProviderFetchOutcome.Success(snapshot)));
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("test", strategy));
        var service = CreateService(registry);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "test", Label = "x" };

        var outcome = await service.RefreshInstanceAsync(instance);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal(50, success.Snapshot.Primary.UsedPercent);
        Assert.Equal(outcome, service.GetLatest(instance.InstanceId));
    }

    [Fact]
    public async Task RefreshInstanceAsync_SkipsUnavailableStrategies_AndFallsThroughToNext()
    {
        var snapshot = new UsageSnapshot(new UsageWindow(10, null, "Session"), null, null, DateTimeOffset.UtcNow, "fallback");
        var unavailable = new FakeFetchStrategy((_, _) => throw new InvalidOperationException("should not be called"), isAvailable: false);
        var fallback = new FakeFetchStrategy((_, _) => Task.FromResult<ProviderFetchOutcome>(new ProviderFetchOutcome.Success(snapshot)));
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("test", unavailable, fallback));
        var service = CreateService(registry);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "test", Label = "x" };

        var outcome = await service.RefreshInstanceAsync(instance);

        var success = Assert.IsType<ProviderFetchOutcome.Success>(outcome);
        Assert.Equal("fallback", success.Snapshot.SourceLabel);
        Assert.Equal(0, unavailable.FetchCallCount);
    }

    [Fact]
    public async Task RefreshInstanceAsync_CoalescesConcurrentCallsForSameInstance()
    {
        var gate = new TaskCompletionSource();
        var callCount = 0;
        var strategy = new FakeFetchStrategy(async (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            await gate.Task;
            return new ProviderFetchOutcome.Success(new UsageSnapshot(new UsageWindow(1, null, "s"), null, null, DateTimeOffset.UtcNow, "src"));
        });
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("test", strategy));
        var service = CreateService(registry);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "test", Label = "x" };

        var first = service.RefreshInstanceAsync(instance);
        var second = service.RefreshInstanceAsync(instance);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RefreshInstanceAsync_ReturnsCachedOutcome_WhenCalledAgainWithinMinInterval()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var callCount = 0;
        var strategy = new FakeFetchStrategy((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<ProviderFetchOutcome>(new ProviderFetchOutcome.Success(
                new UsageSnapshot(new UsageWindow(1, null, "s"), null, null, DateTimeOffset.UtcNow, "src")));
        });
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("test", strategy));
        var service = CreateService(registry, minRefreshInterval: TimeSpan.FromSeconds(20), now: clock.Now);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "test", Label = "x" };

        await service.RefreshInstanceAsync(instance);
        clock.Advance(TimeSpan.FromSeconds(5));
        await service.RefreshInstanceAsync(instance);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RefreshInstanceAsync_FetchesAgain_OnceMinIntervalHasPassed()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var callCount = 0;
        var strategy = new FakeFetchStrategy((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<ProviderFetchOutcome>(new ProviderFetchOutcome.Success(
                new UsageSnapshot(new UsageWindow(1, null, "s"), null, null, DateTimeOffset.UtcNow, "src")));
        });
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("test", strategy));
        var service = CreateService(registry, minRefreshInterval: TimeSpan.FromSeconds(20), now: clock.Now);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "test", Label = "x" };

        await service.RefreshInstanceAsync(instance);
        clock.Advance(TimeSpan.FromSeconds(21));
        await service.RefreshInstanceAsync(instance);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task RefreshInstanceAsync_SkipsFetch_WhileRateLimitedAfter429()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var callCount = 0;
        var retryAfter = clock.Now().AddMinutes(5);
        var strategy = new FakeFetchStrategy((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<ProviderFetchOutcome>(
                new ProviderFetchOutcome.Failure("rate limited", StatusCode: System.Net.HttpStatusCode.TooManyRequests, RetryAfter: retryAfter));
        });
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("test", strategy));
        // Use a near-zero min interval so only the 429 backoff (not the generic throttle) is under test.
        var service = CreateService(registry, minRefreshInterval: TimeSpan.Zero, now: clock.Now);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "test", Label = "x" };

        await service.RefreshInstanceAsync(instance);
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.RefreshInstanceAsync(instance);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RefreshInstanceAsync_FetchesAgain_AfterRetryAfterHasPassed()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var callCount = 0;
        var retryAfter = clock.Now().AddMinutes(5);
        var strategy = new FakeFetchStrategy((_, _) =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            ProviderFetchOutcome outcome = attempt == 1
                ? new ProviderFetchOutcome.Failure("rate limited", StatusCode: System.Net.HttpStatusCode.TooManyRequests, RetryAfter: retryAfter)
                : new ProviderFetchOutcome.Success(new UsageSnapshot(new UsageWindow(1, null, "s"), null, null, DateTimeOffset.UtcNow, "src"));
            return Task.FromResult(outcome);
        });
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("test", strategy));
        var service = CreateService(registry, minRefreshInterval: TimeSpan.Zero, now: clock.Now);
        var instance = new ProviderInstance { InstanceId = Guid.NewGuid(), ProviderId = "test", Label = "x" };

        await service.RefreshInstanceAsync(instance);
        clock.Advance(TimeSpan.FromMinutes(6));
        var second = await service.RefreshInstanceAsync(instance);

        Assert.Equal(2, callCount);
        Assert.IsType<ProviderFetchOutcome.Success>(second);
    }
}
