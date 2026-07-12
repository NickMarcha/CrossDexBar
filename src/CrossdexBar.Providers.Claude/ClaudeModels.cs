using System.Text.Json.Serialization;

namespace CrossdexBar.Providers.Claude;

// Shapes verified against steipete/CodexBar's Swift source (ClaudeOAuthCredentialModels.swift,
// ClaudeOAuthUsageFetcher.swift as of 2026-07): the credentials file uses camelCase keys, the usage
// API response uses snake_case keys. Anthropic hasn't published either publicly, so these could still
// drift from CodexBar's own source over time.

internal sealed class ClaudeCredentialsFile
{
    [JsonPropertyName("claudeAiOauth")]
    public ClaudeOAuthCredentials? ClaudeAiOauth { get; set; }
}

internal sealed class ClaudeOAuthCredentials
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }
}

internal sealed class ClaudeOAuthUsageResponse
{
    [JsonPropertyName("five_hour")]
    public ClaudeOAuthUsageWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public ClaudeOAuthUsageWindow? SevenDay { get; set; }
}

internal sealed class ClaudeOAuthUsageWindow
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; set; }
}
