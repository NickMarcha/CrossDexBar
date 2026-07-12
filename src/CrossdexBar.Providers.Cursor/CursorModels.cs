using System.Text.Json.Serialization;

namespace CrossdexBar.Providers.Cursor;

// Shapes verified against steipete/CodexBar's Swift source (CursorStatusProbe.swift as of 2026-07):
// `GET /api/usage-summary` with a `WorkosCursorSessionToken` cookie derived from the local Cursor.app session.

internal sealed class CursorUsageSummaryResponse
{
    [JsonPropertyName("billingCycleEnd")]
    public string? BillingCycleEnd { get; set; }

    [JsonPropertyName("individualUsage")]
    public CursorIndividualUsage? IndividualUsage { get; set; }
}

internal sealed class CursorIndividualUsage
{
    [JsonPropertyName("plan")]
    public CursorPlanUsage? Plan { get; set; }
}

internal sealed class CursorPlanUsage
{
    [JsonPropertyName("used")]
    public long? Used { get; set; }

    [JsonPropertyName("limit")]
    public long? Limit { get; set; }

    [JsonPropertyName("totalPercentUsed")]
    public double? TotalPercentUsed { get; set; }
}
