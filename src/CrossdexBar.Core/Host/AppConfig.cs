namespace CrossdexBar.Core.Host;

public sealed class AppConfig
{
    public int RefreshIntervalSeconds { get; set; } = 300;
    public List<ProviderInstanceConfig> Providers { get; set; } = new();
}

public sealed class ProviderInstanceConfig
{
    public Guid InstanceId { get; set; }
    public string ProviderId { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = new();
}
