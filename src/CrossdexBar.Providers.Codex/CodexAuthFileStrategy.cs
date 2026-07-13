using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Codex;

/// <summary>
/// Reads the OAuth access token the official `codex` CLI already wrote to `auth.json` (default
/// <c>~/.codex/auth.json</c>, or <c>$CODEX_HOME/auth.json</c>) and calls Codex's usage endpoint with it.
/// An instance's <c>authFilePath</c> setting overrides the path, which is what makes multiple Codex
/// account instances possible: point a second instance at a second copy of the credential file.
/// </summary>
public sealed class CodexAuthFileStrategy : IFetchStrategy
{
    private const string SignInMessage = "No Codex credentials found. Run `codex login`, or set a custom auth.json path in Settings.";
    private const string SessionExpiredMessage = "Codex session expired. Run `codex login` again to refresh it.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri UsageEndpoint = new("https://chatgpt.com/backend-api/wham/usage");

    public string Id => "codex.auth-file";

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct) =>
        Task.FromResult(File.Exists(ResolveAuthFilePath(context)));

    public async Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct)
    {
        var authFilePath = ResolveAuthFilePath(context);
        if (!File.Exists(authFilePath))
            return new ProviderFetchOutcome.NotSignedIn(SignInMessage);

        CodexAuthFile? auth;
        try
        {
            var json = await File.ReadAllTextAsync(authFilePath, ct);
            auth = JsonSerializer.Deserialize<CodexAuthFile>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new ProviderFetchOutcome.Failure($"Could not read Codex credentials: {ex.Message}", ex);
        }

        if (auth?.Tokens?.AccessToken is not { Length: > 0 } accessToken)
            return new ProviderFetchOutcome.NotSignedIn(SignInMessage);

        var outcome = await FetchUsageAsync(context, accessToken, ct);
        return outcome is ProviderFetchOutcome.Failure { StatusCode: HttpStatusCode.Unauthorized }
            ? new ProviderFetchOutcome.NotSignedIn(SessionExpiredMessage)
            : outcome;
    }

    private static async Task<ProviderFetchOutcome> FetchUsageAsync(ProviderFetchContext context, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await context.HttpApi.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return new ProviderFetchOutcome.Failure($"Could not reach the Codex usage API: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
            return new ProviderFetchOutcome.Failure($"Codex usage API returned {(int)response.StatusCode} {response.ReasonPhrase}.", StatusCode: response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        var usage = JsonSerializer.Deserialize<CodexUsageResponse>(body, JsonOptions);
        if (usage?.RateLimit is not { } rateLimit)
            return new ProviderFetchOutcome.Failure("Codex usage API response did not include rate-limit data.");

        // Weekly-only plans omit primary_window; promote secondary_window to primary in that case (matches
        // the official client's fallback), leaving no secondary window.
        UsageWindow primary;
        UsageWindow? secondary = null;
        if (rateLimit.PrimaryWindow is { } primaryWindow)
        {
            primary = ToUsageWindow(primaryWindow, "Session");
            if (rateLimit.SecondaryWindow is { } secondaryWindow)
                secondary = ToUsageWindow(secondaryWindow, "Weekly");
        }
        else if (rateLimit.SecondaryWindow is { } weeklyOnly)
        {
            primary = ToUsageWindow(weeklyOnly, "Weekly");
        }
        else
        {
            return new ProviderFetchOutcome.Failure("Codex usage API response did not include rate-limit data.");
        }

        var snapshot = new UsageSnapshot(primary, secondary, null, DateTimeOffset.UtcNow, "auth-file");
        return new ProviderFetchOutcome.Success(snapshot);
    }

    private static UsageWindow ToUsageWindow(CodexRateWindow window, string label) => new(
        UsedPercent: window.UsedPercent,
        ResetsAt: window.ResetAtUnixSeconds is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
        Label: label);

    private static string ResolveAuthFilePath(ProviderFetchContext context)
    {
        if (context.GetSetting("authFilePath") is { Length: > 0 } custom)
            return custom;

        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var baseDir = string.IsNullOrEmpty(codexHome) ? Path.Combine(context.PlatformPaths.HomeDirectory, ".codex") : codexHome;
        return Path.Combine(baseDir, "auth.json");
    }
}
