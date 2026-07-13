using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Claude;

/// <summary>
/// Reads the OAuth access token the official `claude` CLI already wrote to its credentials file (default
/// <c>~/.claude/.credentials.json</c>) and calls Claude's OAuth usage endpoint with it. An instance's
/// <c>credentialsFilePath</c> setting overrides the path, same pattern as Codex's <c>authFilePath</c>, so a
/// second instance can point at a second account's credentials file.
/// </summary>
public sealed class ClaudeAuthFileStrategy : IFetchStrategy
{
    private const string SignInMessage = "No Claude credentials found. Run `claude` to sign in, or set a custom credentials file path in Settings.";
    private const string SessionExpiredMessage = "Claude session expired. Run `claude` to re-authenticate.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri UsageEndpoint = new("https://api.anthropic.com/api/oauth/usage");

    public string Id => "claude.auth-file";

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct) =>
        Task.FromResult(File.Exists(ResolveCredentialsFilePath(context)));

    public async Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct)
    {
        var credentialsPath = ResolveCredentialsFilePath(context);
        if (!File.Exists(credentialsPath))
            return new ProviderFetchOutcome.NotSignedIn(SignInMessage);

        ClaudeCredentialsFile? credentials;
        try
        {
            var json = await File.ReadAllTextAsync(credentialsPath, ct);
            credentials = JsonSerializer.Deserialize<ClaudeCredentialsFile>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new ProviderFetchOutcome.Failure($"Could not read Claude credentials: {ex.Message}", ex);
        }

        if (credentials?.ClaudeAiOauth?.AccessToken is not { Length: > 0 } accessToken)
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
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        HttpResponseMessage response;
        try
        {
            response = await context.HttpApi.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return new ProviderFetchOutcome.Failure($"Could not reach the Claude usage API: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests ? ResolveRetryAfter(response) : null;
            var suffix = retryAfter is { } at ? $" Retrying after {at.ToLocalTime():t}." : "";
            return new ProviderFetchOutcome.Failure(
                $"Claude usage API returned {(int)response.StatusCode} {response.ReasonPhrase}.{suffix}",
                StatusCode: response.StatusCode,
                RetryAfter: retryAfter);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var usage = JsonSerializer.Deserialize<ClaudeOAuthUsageResponse>(body, JsonOptions);
        if (usage is null)
            return new ProviderFetchOutcome.Failure("Claude usage API response was empty or malformed.");

        UsageWindow primary;
        UsageWindow? secondary = null;
        UsageWindow? tertiary = null;

        var sessionLimit = usage.Limits?.FirstOrDefault(limit => string.Equals(limit.Kind, "session", StringComparison.OrdinalIgnoreCase));
        var weeklyAllLimit = usage.Limits?.FirstOrDefault(limit => string.Equals(limit.Kind, "weekly_all", StringComparison.OrdinalIgnoreCase));
        var weeklyScopedLimit = usage.Limits?.FirstOrDefault(limit => string.Equals(limit.Kind, "weekly_scoped", StringComparison.OrdinalIgnoreCase));

        if (sessionLimit is { Percent: not null } session)
        {
            primary = ToUsageWindow(session, "Session");
            if (weeklyAllLimit is { Percent: not null } weeklyAll)
                secondary = ToUsageWindow(weeklyAll, "Weekly");
            if (weeklyScopedLimit is { Percent: not null } weeklyScoped)
                tertiary = ToUsageWindow(weeklyScoped, "Fable");
        }
        else if (weeklyAllLimit is { Percent: not null } weeklyAllOnly)
        {
            primary = ToUsageWindow(weeklyAllOnly, "Weekly");
            if (weeklyScopedLimit is { Percent: not null } weeklyScoped)
                tertiary = ToUsageWindow(weeklyScoped, "Fable");
        }
        else if (usage.FiveHour is { Utilization: not null } fiveHour)
        {
            primary = ToUsageWindow(fiveHour, "Session");
            if (usage.SevenDay is { Utilization: not null } sevenDay)
                secondary = ToUsageWindow(sevenDay, "Weekly");
        }
        else if (usage.SevenDay is { Utilization: not null } sevenDayOnly)
        {
            primary = ToUsageWindow(sevenDayOnly, "Weekly");
        }
        else
        {
            return new ProviderFetchOutcome.Failure("Claude usage API response did not include any usage windows.");
        }

        return new ProviderFetchOutcome.Success(new UsageSnapshot(primary, secondary, tertiary, DateTimeOffset.UtcNow, "auth-file"));
    }

    private static DateTimeOffset? ResolveRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
            return null;
        if (retryAfter.Date is { } date)
            return date;
        return retryAfter.Delta is { } delta ? DateTimeOffset.UtcNow.Add(delta) : null;
    }

    private static UsageWindow ToUsageWindow(ClaudeOAuthUsageWindow window, string label) => new(
        UsedPercent: window.Utilization!.Value,
        ResetsAt: window.ResetsAt,
        Label: label);

    private static UsageWindow ToUsageWindow(ClaudeOAuthLimitWindow window, string label) => new(
        UsedPercent: window.Percent!.Value,
        ResetsAt: window.ResetsAt,
        Label: label);

    private static string ResolveCredentialsFilePath(ProviderFetchContext context) =>
        context.GetSetting("credentialsFilePath") is { Length: > 0 } custom
            ? custom
            : Path.Combine(context.PlatformPaths.HomeDirectory, ".claude", ".credentials.json");
}
