using System.Text.Json.Serialization;

namespace CrossdexBar.Providers.Copilot;

internal sealed class CopilotUserResponse
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("copilot_plan")]
    public string? CopilotPlan { get; set; }

    [JsonPropertyName("quota_snapshots")]
    public Dictionary<string, CopilotQuotaSnapshot>? QuotaSnapshots { get; set; }

    [JsonPropertyName("limited_user_quotas")]
    public Dictionary<string, double>? LimitedUserQuotas { get; set; }

    [JsonPropertyName("monthly_quotas")]
    public Dictionary<string, double>? MonthlyQuotas { get; set; }

    [JsonPropertyName("quota_reset_date")]
    public string? QuotaResetDate { get; set; }

    [JsonPropertyName("quota_reset_date_utc")]
    public string? QuotaResetDateUtc { get; set; }

    [JsonPropertyName("limited_user_reset_date")]
    public string? LimitedUserResetDate { get; set; }
}

internal sealed class CopilotQuotaSnapshot
{
    [JsonPropertyName("has_quota")]
    public bool? HasQuota { get; set; }

    [JsonPropertyName("unlimited")]
    public bool? Unlimited { get; set; }

    [JsonPropertyName("percent_remaining")]
    public double? PercentRemaining { get; set; }

    [JsonPropertyName("remaining")]
    public double? Remaining { get; set; }

    [JsonPropertyName("entitlement")]
    public double? Entitlement { get; set; }
}
