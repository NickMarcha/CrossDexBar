namespace CrossdexBar.Core.Providers;

/// <summary>
/// Explicit, exhaustive registry of every provider type the app knows about. Registration happens once,
/// by hand, in the app's composition root (which is the only project that references every provider
/// package) — deliberately not reflection/assembly-scanning, so the set of active providers is always
/// visible by reading one file.
/// </summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<string, ProviderDescriptor> _descriptors = new();

    public void Register(ProviderDescriptor descriptor)
    {
        if (!_descriptors.TryAdd(descriptor.Id, descriptor))
            throw new InvalidOperationException($"Provider '{descriptor.Id}' is already registered.");
    }

    public bool TryGet(string providerId, out ProviderDescriptor descriptor) =>
        _descriptors.TryGetValue(providerId, out descriptor!);

    public ProviderDescriptor Get(string providerId) => _descriptors[providerId];

    public IReadOnlyCollection<ProviderDescriptor> All => _descriptors.Values;
}
