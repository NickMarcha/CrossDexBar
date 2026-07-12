using System.Net;

namespace CrossdexBar.Core.Providers;

/// <summary>
/// Result of one fetch attempt. A distinct "not signed in" case (vs. a generic failure) lets the UI
/// show an actionable "sign in" message instead of a raw error for the most common non-error state.
/// </summary>
public abstract record ProviderFetchOutcome
{
    public sealed record Success(UsageSnapshot Snapshot) : ProviderFetchOutcome;

    public sealed record NotSignedIn(string Message) : ProviderFetchOutcome;

    /// <summary>Credentials are valid but no usage percentage could be obtained (distinct from an error: nothing is broken, the data just isn't exposed through this fetch path).</summary>
    public sealed record Unavailable(string Message) : ProviderFetchOutcome;

    /// <summary>
    /// <paramref name="RetryAfter"/> carries the server's own backoff hint (e.g. a 429 response's
    /// <c>Retry-After</c> header) so <see cref="Refresh.RefreshService"/> can avoid hammering the endpoint
    /// again before then, instead of retrying blindly on the next timer tick or user click.
    /// </summary>
    public sealed record Failure(
        string Message, Exception? Exception = null, HttpStatusCode? StatusCode = null, DateTimeOffset? RetryAfter = null)
        : ProviderFetchOutcome;
}
