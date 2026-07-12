using System.Text.Json.Serialization;

namespace CrossdexBar.Providers.Codex;

// Shapes verified against Finesssee/Win-CodexBar's Rust source (rust/src/providers/codex/api.rs as of
// 2026-07): auth.json uses snake_case under "tokens", and the usage response's window reset field is
// "reset_at" — an absolute Unix timestamp in seconds, not a relative "seconds until reset" duration.

internal sealed class CodexAuthFile
{
    [JsonPropertyName("tokens")]
    public CodexTokens? Tokens { get; set; }
}

internal sealed class CodexTokens
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}

internal sealed class CodexUsageResponse
{
    [JsonPropertyName("rate_limit")]
    public CodexRateLimit? RateLimit { get; set; }
}

internal sealed class CodexRateLimit
{
    [JsonPropertyName("primary_window")]
    public CodexRateWindow? PrimaryWindow { get; set; }

    [JsonPropertyName("secondary_window")]
    public CodexRateWindow? SecondaryWindow { get; set; }
}

internal sealed class CodexRateWindow
{
    [JsonPropertyName("used_percent")]
    public double UsedPercent { get; set; }

    /// <summary>Unix timestamp (seconds) the window resets at — NOT a relative "seconds remaining" duration.</summary>
    [JsonPropertyName("reset_at")]
    public long? ResetAtUnixSeconds { get; set; }
}
