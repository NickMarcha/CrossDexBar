using CrossdexBar.Core.Providers;

namespace CrossdexBar.Core.Tests.Fakes;

internal sealed class FakeFetchStrategy(
    Func<ProviderFetchContext, CancellationToken, Task<ProviderFetchOutcome>> fetch, bool isAvailable = true)
    : IFetchStrategy
{
    public int FetchCallCount { get; private set; }

    public string Id => "fake";

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct) => Task.FromResult(isAvailable);

    public Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct)
    {
        FetchCallCount++;
        return fetch(context, ct);
    }
}
