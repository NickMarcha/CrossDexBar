using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Copilot;

/// <summary>
/// Uses the active GitHub CLI login (`gh auth token`) to read Copilot quota snapshots from
/// `https://api.github.com/copilot_internal/user`.
/// </summary>
public sealed class CopilotGhCliStrategy : IFetchStrategy
{
    private const string SignInMessage = "No GitHub CLI credentials found. Run `gh auth login` to sign in.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri UserEndpoint = new("https://api.github.com/copilot_internal/user");

    public string Id => "copilot.gh-cli";

    public async Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct)
    {
        try
        {
            var status = await context.CliRunner.RunAsync("gh", ["auth", "status"], TimeSpan.FromSeconds(10), ct);
            return status.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct)
    {
        string token;
        try
        {
            var tokenResult = await context.CliRunner.RunAsync("gh", ["auth", "token"], TimeSpan.FromSeconds(10), ct);
            token = tokenResult.StdOut.Trim();
            if (tokenResult.ExitCode != 0 || string.IsNullOrWhiteSpace(token))
                return new ProviderFetchOutcome.NotSignedIn(SignInMessage);
        }
        catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception)
        {
            return new ProviderFetchOutcome.NotSignedIn($"{SignInMessage} ({ex.Message})");
        }

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("editor-plugin-version", "crossdexbar/0.1");
            request.Headers.Add("editor-version", "CrossdexBar/0.1");
            request.Headers.Add("x-github-api-version", "2025-04-01");
            request.Headers.UserAgent.ParseAdd("crossdexbar/0.1");
            response = await context.HttpApi.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return new ProviderFetchOutcome.Failure($"Could not reach GitHub Copilot usage API: {ex.Message}", ex);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new ProviderFetchOutcome.NotSignedIn(SignInMessage);

        if (!response.IsSuccessStatusCode)
            return new ProviderFetchOutcome.Failure($"GitHub Copilot usage API returned {(int)response.StatusCode} {response.ReasonPhrase}.", StatusCode: response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        CopilotUserResponse? usage;
        try
        {
            usage = JsonSerializer.Deserialize<CopilotUserResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ProviderFetchOutcome.Failure($"Could not parse Copilot usage response: {ex.Message}", ex);
        }

        if (usage is null)
            return new ProviderFetchOutcome.Failure("GitHub Copilot usage API response was empty or malformed.");

        var resetsAt = ParseResetAt(usage);
        var windows = CollectWindows(usage, resetsAt).Take(3).ToList();
        if (windows.Count == 0)
            return new ProviderFetchOutcome.Unavailable("Signed in, but Copilot did not report measurable quota usage windows for this account.");

        var snapshot = new UsageSnapshot(
            Primary: windows[0],
            Secondary: windows.Count > 1 ? windows[1] : null,
            Tertiary: windows.Count > 2 ? windows[2] : null,
            UpdatedAt: DateTimeOffset.UtcNow,
            SourceLabel: "gh-cli",
            Identity: new ProviderIdentity(Email: usage.Login, Plan: usage.CopilotPlan));
        return new ProviderFetchOutcome.Success(snapshot);
    }

    private static IEnumerable<UsageWindow> CollectWindows(CopilotUserResponse usage, DateTimeOffset? resetsAt)
    {
        var ranked = new List<(int Rank, UsageWindow Window)>();
        if (usage.QuotaSnapshots is { Count: > 0 } snapshots)
        {
            foreach (var (category, snapshot) in snapshots)
            {
                if (snapshot is null || snapshot.HasQuota == false || snapshot.Unlimited == true)
                    continue;

                var percentRemaining = snapshot.PercentRemaining;
                if (percentRemaining is null && snapshot.Entitlement is > 0 && snapshot.Remaining is { } remaining)
                    percentRemaining = remaining / snapshot.Entitlement.Value * 100;
                if (percentRemaining is null)
                    continue;

                var usedPercent = Math.Clamp(100 - percentRemaining.Value, 0, 100);
                ranked.Add((RankForCategory(category), new UsageWindow(usedPercent, resetsAt, LabelForCategory(category))));
            }
        }

        if (ranked.Count == 0 && usage.MonthlyQuotas is { Count: > 0 } monthly)
        {
            foreach (var (category, entitlement) in monthly)
            {
                if (entitlement <= 0)
                    continue;

                var remaining = 0d;
                if (usage.LimitedUserQuotas is { } limited)
                    limited.TryGetValue(category, out remaining);
                var usedPercent = Math.Clamp((entitlement - remaining) / entitlement * 100, 0, 100);
                ranked.Add((RankForCategory(category), new UsageWindow(usedPercent, resetsAt, LabelForCategory(category))));
            }
        }

        return ranked.OrderBy(window => window.Rank).Select(window => window.Window);
    }

    private static string LabelForCategory(string category) => category switch
    {
        "premium_interactions" => "Premium",
        "chat" => "Chat",
        "completions" => "Completions",
        _ => category.Replace('_', ' ')
    };

    private static int RankForCategory(string category) => category switch
    {
        "premium_interactions" => 0,
        "chat" => 1,
        "completions" => 2,
        _ => 10,
    };

    private static DateTimeOffset? ParseResetAt(CopilotUserResponse usage)
    {
        if (TryParseDateTimeOffset(usage.QuotaResetDateUtc, out var absolute))
            return absolute;
        if (TryParseDateTimeOffset(usage.LimitedUserResetDate, out absolute))
            return absolute;

        if (!string.IsNullOrWhiteSpace(usage.QuotaResetDate)
            && DateTime.TryParseExact(usage.QuotaResetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dateOnly))
        {
            var utc = DateTime.SpecifyKind(dateOnly, DateTimeKind.Utc);
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }

        return null;
    }

    private static bool TryParseDateTimeOffset(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            return true;
        if (DateTime.TryParseExact(value, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dateTime))
        {
            result = new DateTimeOffset(dateTime, TimeSpan.Zero);
            return true;
        }

        return false;
    }
}
