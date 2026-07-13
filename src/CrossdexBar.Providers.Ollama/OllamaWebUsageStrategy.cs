using System.Net;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Ollama;

/// <summary>
/// Fetches real Ollama Cloud Usage quota bars by scraping <c>ollama.com/settings</c> with a manually
/// pasted browser session cookie — the only way either reference app gets a real percentage for Ollama;
/// there's no documented API for it. Tried before <see cref="OllamaApiKeyStrategy"/> in the pipeline since
/// it's the only strategy that can return real numbers instead of just validating a key.
/// </summary>
public sealed class OllamaWebUsageStrategy : IFetchStrategy
{
    private const string SignInMessage = "No Ollama cookie configured. Paste a Cookie header from ollama.com/settings in Settings to see real usage.";
    private const string SessionExpiredMessage = "Ollama cookie was rejected or has expired. Paste a fresh Cookie header from ollama.com/settings in Settings.";

    private static readonly Uri SettingsEndpoint = new("https://ollama.com/settings");

    public string Id => "ollama.web-cookie";

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct) =>
        Task.FromResult(context.GetSetting("cookieHeader") is { Length: > 0 });

    public async Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct)
    {
        if (context.GetSetting("cookieHeader") is not { Length: > 0 } cookieHeader)
            return new ProviderFetchOutcome.NotSignedIn(SignInMessage);

        using var request = new HttpRequestMessage(HttpMethod.Get, SettingsEndpoint);
        request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("Accept", "text/html");

        HttpResponseMessage response;
        try
        {
            response = await context.HttpApi.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return new ProviderFetchOutcome.Failure($"Could not reach ollama.com: {ex.Message}", ex);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new ProviderFetchOutcome.NotSignedIn(SessionExpiredMessage);

        if (!response.IsSuccessStatusCode)
            return new ProviderFetchOutcome.Failure($"Ollama settings page returned {(int)response.StatusCode} {response.ReasonPhrase}.", StatusCode: response.StatusCode);

        var html = await response.Content.ReadAsStringAsync(ct);
        var session = OllamaUsagePageParser.ParseBlock(html, "Session usage", "Hourly usage");
        var weekly = OllamaUsagePageParser.ParseBlock(html, "Weekly usage");

        if (session is null && weekly is null)
        {
            return new ProviderFetchOutcome.Failure(
                "Could not find usage data on the Ollama settings page (its layout may have changed, or the pasted cookie is for a signed-out session).");
        }

        UsageWindow primary;
        UsageWindow? secondary = null;
        if (session is { } sessionBlock)
        {
            primary = new UsageWindow(sessionBlock.UsedPercent, sessionBlock.ResetsAt, "Session");
            if (weekly is { } weeklyBlock)
                secondary = new UsageWindow(weeklyBlock.UsedPercent, weeklyBlock.ResetsAt, "Weekly");
        }
        else
        {
            var weeklyBlock = weekly!.Value;
            primary = new UsageWindow(weeklyBlock.UsedPercent, weeklyBlock.ResetsAt, "Weekly");
        }

        return new ProviderFetchOutcome.Success(new UsageSnapshot(primary, secondary, null, DateTimeOffset.UtcNow, "web-cookie"));
    }
}
