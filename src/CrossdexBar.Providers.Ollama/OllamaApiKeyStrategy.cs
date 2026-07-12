using System.Net;
using System.Net.Http.Headers;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Ollama;

/// <summary>
/// Validates an Ollama Cloud API key against <c>GET /api/tags</c>. Verified against steipete/CodexBar's
/// Swift source (OllamaUsageFetcher.swift as of 2026-07): even CodexBar's own API-key path returns no
/// usage percentage at all — Ollama's Cloud Usage quota bars are only exposed on the (cookie-authenticated)
/// <c>ollama.com/settings</c> HTML page, which is out of scope here (see plan). So a valid key here confirms
/// the key works but can't show a percentage.
/// </summary>
public sealed class OllamaApiKeyStrategy : IFetchStrategy
{
    private static readonly Uri TagsEndpoint = new("https://ollama.com/api/tags");

    public string Id => "ollama.api-key";

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct) =>
        Task.FromResult(ResolveApiKey(context) is { Length: > 0 });

    public async Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct)
    {
        if (ResolveApiKey(context) is not { Length: > 0 } apiKey)
        {
            return new ProviderFetchOutcome.NotSignedIn(
                "No Ollama API key configured. Add one from https://ollama.com/settings/keys in Settings, or set OLLAMA_API_KEY.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, TagsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await context.HttpApi.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return new ProviderFetchOutcome.Failure($"Could not reach the Ollama API: {ex.Message}", ex);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new ProviderFetchOutcome.NotSignedIn("Ollama API key was rejected. Check the key in Settings.");

        if (!response.IsSuccessStatusCode)
            return new ProviderFetchOutcome.Failure($"Ollama API returned {(int)response.StatusCode} {response.ReasonPhrase}.", StatusCode: response.StatusCode);

        return new ProviderFetchOutcome.Unavailable(
            "API key is valid — Ollama doesn't expose usage/quota data through the API. Check Cloud Usage at https://ollama.com/settings.");
    }

    private static string? ResolveApiKey(ProviderFetchContext context) =>
        context.GetSetting("apiKey") is { Length: > 0 } configured
            ? configured
            : Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
}
