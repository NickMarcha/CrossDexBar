namespace CrossdexBar.Core.Providers;

public sealed record ProviderBranding(string ColorHex, string IconResourceName);

public sealed record ProviderCapabilities(bool SupportsSecondaryWindow = true, bool SupportsCredits = false);

/// <summary>Describes one per-instance setting a provider needs (e.g. an API key or a credential-file path override).</summary>
public sealed record ProviderInstanceSettingField(string Key, string Label, bool Required, string? Placeholder = null);

/// <summary>
/// Single source of truth for one provider type: labels, branding, capabilities, the per-instance settings
/// it needs, and its ordered fetch-strategy pipeline. Adding a provider means creating one of these plus its
/// strategies, then adding one line to a <see cref="ProviderRegistry"/> — no reflection-based discovery.
/// </summary>
public sealed class ProviderDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required ProviderBranding Branding { get; init; }
    public ProviderCapabilities Capabilities { get; init; } = new();
    public IReadOnlyList<ProviderInstanceSettingField> InstanceSettingsSchema { get; init; } = [];
    public required IReadOnlyList<IFetchStrategy> Strategies { get; init; }
}
