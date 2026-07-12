using System.Collections.Concurrent;
using System.Net;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.Core.Refresh;

public sealed class InstanceRefreshedEventArgs(Guid instanceId, ProviderFetchOutcome outcome) : EventArgs
{
    public Guid InstanceId { get; } = instanceId;
    public ProviderFetchOutcome Outcome { get; } = outcome;
}

/// <summary>
/// Drives background + manual refreshes for provider instances. Concurrent refresh requests for the same
/// instance share one in-flight fetch instead of racing; each instance refreshes independently of the others.
///
/// Two throttles keep it from hammering a provider's API: a minimum interval between any two fetch attempts
/// for the same instance (protects against e.g. rapid popover open/close or double-clicking refresh), and a
/// 429-aware backoff that skips real fetches until the server's own <c>Retry-After</c> hint (or a default
/// cooldown) has passed. Both return the last cached outcome instead of erroring, so the UI still shows
/// something sensible during the cooldown.
/// </summary>
public sealed class RefreshService : IDisposable
{
    private static readonly TimeSpan DefaultMinRefreshInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromMinutes(2);

    private readonly ProviderRegistry _registry;
    private readonly ICliRunner _cliRunner;
    private readonly IHttpApi _httpApi;
    private readonly IConfigStore _configStore;
    private readonly IPlatformPaths _platformPaths;
    private readonly TimeSpan _minRefreshInterval;
    private readonly TimeSpan _defaultRateLimitBackoff;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<Guid, Task<ProviderFetchOutcome>> _inFlight = new();
    private readonly ConcurrentDictionary<Guid, ProviderFetchOutcome> _latest = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastAttemptAt = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _blockedUntil = new();
    private CancellationTokenSource? _loopCts;

    public event EventHandler<InstanceRefreshedEventArgs>? InstanceRefreshed;

    public RefreshService(
        ProviderRegistry registry,
        ICliRunner cliRunner,
        IHttpApi httpApi,
        IConfigStore configStore,
        IPlatformPaths platformPaths,
        TimeSpan? minRefreshInterval = null,
        TimeSpan? defaultRateLimitBackoff = null,
        Func<DateTimeOffset>? now = null)
    {
        _registry = registry;
        _cliRunner = cliRunner;
        _httpApi = httpApi;
        _configStore = configStore;
        _platformPaths = platformPaths;
        _minRefreshInterval = minRefreshInterval ?? DefaultMinRefreshInterval;
        _defaultRateLimitBackoff = defaultRateLimitBackoff ?? DefaultRateLimitBackoff;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public ProviderFetchOutcome? GetLatest(Guid instanceId) => _latest.GetValueOrDefault(instanceId);

    public void Start(TimeSpan interval, Func<IReadOnlyList<ProviderInstance>> instancesProvider)
    {
        Stop();
        _loopCts = new CancellationTokenSource();
        _ = RunLoopAsync(interval, instancesProvider, _loopCts.Token);
    }

    public void Stop()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    public Task<ProviderFetchOutcome> RefreshInstanceAsync(ProviderInstance instance, CancellationToken ct = default)
    {
        if (_inFlight.TryGetValue(instance.InstanceId, out var inFlight))
            return inFlight;

        if (TryGetThrottledOutcome(instance.InstanceId, out var throttled))
            return Task.FromResult(throttled);

        var task = _inFlight.GetOrAdd(instance.InstanceId, _ => RunFetchAsync(instance, ct));

        // Removing the in-flight entry here — after GetOrAdd has definitely returned — instead of from
        // inside RunFetchAsync's own body avoids a real race: a fetch strategy that never truly awaits
        // anything (e.g. a NotSignedIn short-circuit after a plain File.Exists check) could let the whole
        // method run synchronously inside GetOrAdd's factory callback, so a self-removal there could fire
        // before GetOrAdd had actually inserted the entry — leaving a permanently-stuck completed task in
        // _inFlight that freezes that instance's refreshes forever. The conditional remove (only if the
        // dictionary still holds *this exact* task) guards against removing a newer task that already
        // replaced this one by the time this one completes.
        _ = task.ContinueWith(
            _ => _inFlight.TryRemove(new KeyValuePair<Guid, Task<ProviderFetchOutcome>>(instance.InstanceId, task)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        return task;
    }

    private bool TryGetThrottledOutcome(Guid instanceId, out ProviderFetchOutcome outcome)
    {
        outcome = null!;
        if (!_latest.TryGetValue(instanceId, out var cached))
            return false;

        var now = _now();
        if (_blockedUntil.TryGetValue(instanceId, out var blockedUntil) && now < blockedUntil)
        {
            outcome = cached;
            return true;
        }

        if (_lastAttemptAt.TryGetValue(instanceId, out var lastAttempt) && now - lastAttempt < _minRefreshInterval)
        {
            outcome = cached;
            return true;
        }

        return false;
    }

    private async Task RunLoopAsync(TimeSpan interval, Func<IReadOnlyList<ProviderInstance>> instancesProvider, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (true)
        {
            foreach (var instance in instancesProvider().Where(i => i.Enabled))
                await RefreshInstanceAsync(instance, ct);

            try
            {
                if (!await timer.WaitForNextTickAsync(ct))
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<ProviderFetchOutcome> RunFetchAsync(ProviderInstance instance, CancellationToken ct)
    {
        var outcome = await FetchInstanceAsync(instance, ct);
        _lastAttemptAt[instance.InstanceId] = _now();
        UpdateRateLimitState(instance.InstanceId, outcome);
        _latest[instance.InstanceId] = outcome;
        InstanceRefreshed?.Invoke(this, new InstanceRefreshedEventArgs(instance.InstanceId, outcome));
        return outcome;
    }

    private void UpdateRateLimitState(Guid instanceId, ProviderFetchOutcome outcome)
    {
        if (outcome is ProviderFetchOutcome.Failure { StatusCode: HttpStatusCode.TooManyRequests } failure)
            _blockedUntil[instanceId] = failure.RetryAfter ?? _now().Add(_defaultRateLimitBackoff);
        else
            _blockedUntil.TryRemove(instanceId, out _);
    }

    private async Task<ProviderFetchOutcome> FetchInstanceAsync(ProviderInstance instance, CancellationToken ct)
    {
        if (!_registry.TryGet(instance.ProviderId, out var descriptor))
            return new ProviderFetchOutcome.Failure($"Unknown provider '{instance.ProviderId}'.");

        var context = new ProviderFetchContext
        {
            Instance = instance,
            CliRunner = _cliRunner,
            HttpApi = _httpApi,
            ConfigStore = _configStore,
            PlatformPaths = _platformPaths,
        };

        ProviderFetchOutcome? last = null;
        foreach (var strategy in descriptor.Strategies)
        {
            if (!await strategy.IsAvailableAsync(context, ct))
                continue;

            last = await strategy.FetchAsync(context, ct);
            if (last is ProviderFetchOutcome.Success)
                return last;
        }

        return last ?? new ProviderFetchOutcome.Failure("No fetch strategy was available for this provider instance.");
    }

    public void Dispose() => Stop();
}
