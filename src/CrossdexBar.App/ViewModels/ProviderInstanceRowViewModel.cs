using CommunityToolkit.Mvvm.ComponentModel;

namespace CrossdexBar.App.ViewModels;

public sealed partial class ProviderInstanceRowViewModel(Guid instanceId, string providerDisplayName, string label, bool enabled)
    : ObservableObject
{
    public Guid InstanceId { get; } = instanceId;
    public string ProviderDisplayName { get; } = providerDisplayName;

    [ObservableProperty] private string _label = label;
    [ObservableProperty] private bool _enabled = enabled;
}
