namespace CrossdexBar.Core.Providers;

public sealed record UsageWindow(
    double UsedPercent,
    DateTimeOffset? ResetsAt,
    string Label);

public sealed record ProviderIdentity(
    string? Email,
    string? Plan);

public sealed record UsageSnapshot(
    UsageWindow Primary,
    UsageWindow? Secondary,
    UsageWindow? Tertiary,
    DateTimeOffset UpdatedAt,
    string SourceLabel,
    ProviderIdentity? Identity = null);
