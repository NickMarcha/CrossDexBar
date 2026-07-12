using CrossdexBar.Core.Host;

namespace CrossdexBar.Core.Providers;

/// <summary>In-memory list of configured provider instances for the running session, backed by <see cref="IConfigStore"/>.</summary>
public sealed class InstanceStore
{
    private readonly IConfigStore _configStore;
    private readonly List<ProviderInstance> _instances = new();

    public InstanceStore(IConfigStore configStore) => _configStore = configStore;

    public int RefreshIntervalSeconds { get; set; } = 300;

    public IReadOnlyList<ProviderInstance> Instances => _instances;

    public void Load()
    {
        var config = _configStore.Load();
        RefreshIntervalSeconds = config.RefreshIntervalSeconds;
        _instances.Clear();
        _instances.AddRange(config.Providers.Select(ToInstance));
    }

    public void Save()
    {
        var config = new AppConfig
        {
            RefreshIntervalSeconds = RefreshIntervalSeconds,
            Providers = _instances.Select(ToConfig).ToList(),
        };
        _configStore.Save(config);
    }

    public ProviderInstance Add(string providerId, string label, IReadOnlyDictionary<string, string> settings)
    {
        var instance = new ProviderInstance
        {
            InstanceId = Guid.NewGuid(),
            ProviderId = providerId,
            Label = label,
        };
        foreach (var (key, value) in settings)
            instance.Settings[key] = value;

        _instances.Add(instance);
        return instance;
    }

    public void Remove(Guid instanceId) => _instances.RemoveAll(i => i.InstanceId == instanceId);

    private static ProviderInstance ToInstance(ProviderInstanceConfig config) => new()
    {
        InstanceId = config.InstanceId,
        ProviderId = config.ProviderId,
        Label = config.Label,
        Enabled = config.Enabled,
        Settings = new Dictionary<string, string>(config.Settings),
    };

    private static ProviderInstanceConfig ToConfig(ProviderInstance instance) => new()
    {
        InstanceId = instance.InstanceId,
        ProviderId = instance.ProviderId,
        Label = instance.Label,
        Enabled = instance.Enabled,
        Settings = new Dictionary<string, string>(instance.Settings),
    };
}
