using System.Net;
using System.Text.Json;
using CrossdexBar.Core.Providers;
using Microsoft.Data.Sqlite;

namespace CrossdexBar.Providers.Cursor;

/// <summary>
/// Reads Cursor.app's own local session token from its VS Code-style <c>state.vscdb</c> SQLite store (key
/// <c>cursorAuth/accessToken</c> in the <c>ItemTable</c>) — the same file the Cursor desktop app itself
/// maintains, so no browser cookie decryption is needed. An instance's <c>vscdbPath</c> setting overrides
/// the path, same pattern as the other providers, for a second Cursor account/install.
/// </summary>
public sealed class CursorAppAuthStrategy : IFetchStrategy
{
    private const string SignInMessage = "No local Cursor session found. Sign in to the Cursor app first, or set a custom state.vscdb path in Settings.";
    private const string SessionExpiredMessage = "Cursor session expired. Sign in to the Cursor app again.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri UsageSummaryEndpoint = new("https://cursor.com/api/usage-summary");

    public string Id => "cursor.app-auth";

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct) =>
        Task.FromResult(File.Exists(ResolveVscdbPath(context)));

    public async Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct)
    {
        var vscdbPath = ResolveVscdbPath(context);
        if (!File.Exists(vscdbPath))
            return new ProviderFetchOutcome.NotSignedIn(SignInMessage);

        string? accessToken;
        try
        {
            accessToken = ReadAccessToken(vscdbPath);
        }
        catch (Exception ex) when (ex is SqliteException or IOException)
        {
            return new ProviderFetchOutcome.Failure($"Could not read Cursor's local session: {ex.Message}", ex);
        }

        if (accessToken is not { Length: > 0 })
            return new ProviderFetchOutcome.NotSignedIn(SignInMessage);

        CursorAppAuthSession session;
        try
        {
            session = new CursorAppAuthSession(accessToken);
            _ = session.UserId();
        }
        catch (FormatException ex)
        {
            return new ProviderFetchOutcome.Failure($"Cursor's local session token was not in the expected format: {ex.Message}", ex);
        }

        if (session.ExpiresAt() is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            return new ProviderFetchOutcome.NotSignedIn(SessionExpiredMessage);

        var outcome = await FetchUsageAsync(context, session, ct);
        return outcome is ProviderFetchOutcome.Failure { StatusCode: HttpStatusCode.Unauthorized }
            ? new ProviderFetchOutcome.NotSignedIn(SessionExpiredMessage)
            : outcome;
    }

    private static string? ReadAccessToken(string vscdbPath)
    {
        using var connection = new SqliteConnection($"Data Source={vscdbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM ItemTable WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", "cursorAuth/accessToken");
        return command.ExecuteScalar() as string;
    }

    private static async Task<ProviderFetchOutcome> FetchUsageAsync(ProviderFetchContext context, CursorAppAuthSession session, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageSummaryEndpoint);
        request.Headers.Add("Cookie", session.CookieHeader());

        HttpResponseMessage response;
        try
        {
            response = await context.HttpApi.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return new ProviderFetchOutcome.Failure($"Could not reach the Cursor usage API: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
            return new ProviderFetchOutcome.Failure($"Cursor usage API returned {(int)response.StatusCode} {response.ReasonPhrase}.", StatusCode: response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        var summary = JsonSerializer.Deserialize<CursorUsageSummaryResponse>(body, JsonOptions);
        var plan = summary?.IndividualUsage?.Plan;
        if (plan is null)
            return new ProviderFetchOutcome.Failure("Cursor usage API response did not include plan usage data.");

        var usedPercent = plan.TotalPercentUsed
            ?? (plan.Used is { } used && plan.Limit is > 0 ? used / (double)plan.Limit * 100 : 0);
        var resetsAt = DateTimeOffset.TryParse(summary!.BillingCycleEnd, out var parsed) ? parsed : (DateTimeOffset?)null;

        var snapshot = new UsageSnapshot(
            new UsageWindow(usedPercent, resetsAt, "Plan"), null, DateTimeOffset.UtcNow, "app-auth");
        return new ProviderFetchOutcome.Success(snapshot);
    }

    private static string ResolveVscdbPath(ProviderFetchContext context)
    {
        if (context.GetSetting("vscdbPath") is { Length: > 0 } custom)
            return custom;

        return Path.Combine(context.PlatformPaths.AppDataRoot, "Cursor", "User", "globalStorage", "state.vscdb");
    }
}
