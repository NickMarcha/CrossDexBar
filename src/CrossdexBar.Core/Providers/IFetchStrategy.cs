namespace CrossdexBar.Core.Providers;

/// <summary>One concrete way to obtain usage data for a provider instance (e.g. a local credential file).</summary>
public interface IFetchStrategy
{
    string Id { get; }

    Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct);

    Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct);
}
