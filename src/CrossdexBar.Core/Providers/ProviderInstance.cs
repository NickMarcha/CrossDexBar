namespace CrossdexBar.Core.Providers;

/// <summary>
/// A configured account of a given provider type (e.g. one of possibly several Codex logins).
/// This is the unit that gets fetched and rendered as a card; a provider type with zero instances
/// shows nowhere in the UI.
/// </summary>
public sealed class ProviderInstance
{
    public required Guid InstanceId { get; init; }
    public required string ProviderId { get; init; }
    public required string Label { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Settings { get; init; } = new();

    public string? GetSetting(string key) => Settings.GetValueOrDefault(key);
}
