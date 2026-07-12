using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly InstanceStore _instanceStore;
    private readonly ProviderRegistry _registry;
    private readonly IPlatformPaths _platformPaths;
    private readonly Func<IReadOnlyList<ProviderDescriptor>, Task<AddInstanceResult?>> _showAddDialog;
    private readonly Func<ProviderDescriptor, ProviderInstance, Task<EditInstanceResult?>> _showEditDialog;
    private readonly Action _onSaved;

    public ObservableCollection<ProviderInstanceRowViewModel> Instances { get; } = new();
    public IReadOnlyList<int> RefreshIntervalOptions { get; } = [60, 300, 900];

    [ObservableProperty] private int _refreshIntervalSeconds;

    public IAsyncRelayCommand AddAccountCommand { get; }
    public IAsyncRelayCommand<ProviderInstanceRowViewModel> EditInstanceCommand { get; }
    public IRelayCommand<ProviderInstanceRowViewModel> RemoveInstanceCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand OpenConfigFileCommand { get; }

    public SettingsViewModel(
        InstanceStore instanceStore,
        ProviderRegistry registry,
        IPlatformPaths platformPaths,
        Func<IReadOnlyList<ProviderDescriptor>, Task<AddInstanceResult?>> showAddDialog,
        Func<ProviderDescriptor, ProviderInstance, Task<EditInstanceResult?>> showEditDialog,
        Action onSaved)
    {
        _instanceStore = instanceStore;
        _registry = registry;
        _platformPaths = platformPaths;
        _showAddDialog = showAddDialog;
        _showEditDialog = showEditDialog;
        _onSaved = onSaved;

        _refreshIntervalSeconds = instanceStore.RefreshIntervalSeconds;
        RebuildRows();

        AddAccountCommand = new AsyncRelayCommand(AddAccountAsync);
        EditInstanceCommand = new AsyncRelayCommand<ProviderInstanceRowViewModel>(EditInstanceAsync);
        RemoveInstanceCommand = new RelayCommand<ProviderInstanceRowViewModel>(RemoveInstance);
        SaveCommand = new RelayCommand(Save);
        OpenConfigFileCommand = new RelayCommand(OpenConfigFile);
    }

    private void RebuildRows()
    {
        Instances.Clear();
        foreach (var instance in _instanceStore.Instances)
        {
            var displayName = _registry.TryGet(instance.ProviderId, out var descriptor) ? descriptor.DisplayName : instance.ProviderId;
            Instances.Add(new ProviderInstanceRowViewModel(instance.InstanceId, displayName, instance.Label, instance.Enabled));
        }
    }

    private async Task AddAccountAsync()
    {
        var result = await _showAddDialog(_registry.All.ToList());
        if (result is null)
            return;

        _instanceStore.Add(result.ProviderId, result.Label, result.Settings);
        RebuildRows();
    }

    private async Task EditInstanceAsync(ProviderInstanceRowViewModel? row)
    {
        if (row is null)
            return;

        var instance = _instanceStore.Instances.FirstOrDefault(i => i.InstanceId == row.InstanceId);
        if (instance is null || !_registry.TryGet(instance.ProviderId, out var descriptor))
            return;

        var result = await _showEditDialog(descriptor, instance);
        if (result is null)
            return;

        instance.Label = result.Label;
        row.Label = result.Label;
        foreach (var field in descriptor.InstanceSettingsSchema)
        {
            if (result.Settings.TryGetValue(field.Key, out var value))
                instance.Settings[field.Key] = value;
            else
                instance.Settings.Remove(field.Key);
        }
    }

    private void RemoveInstance(ProviderInstanceRowViewModel? row)
    {
        if (row is null)
            return;

        _instanceStore.Remove(row.InstanceId);
        Instances.Remove(row);
    }

    private void Save()
    {
        _instanceStore.RefreshIntervalSeconds = RefreshIntervalSeconds;
        foreach (var row in Instances)
        {
            var instance = _instanceStore.Instances.First(i => i.InstanceId == row.InstanceId);
            instance.Label = row.Label;
            instance.Enabled = row.Enabled;
        }

        _instanceStore.Save();
        _onSaved();
    }

    private void OpenConfigFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_platformPaths.ConfigFilePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or System.IO.FileNotFoundException)
        {
            // No default handler registered for the file, or it doesn't exist yet: nothing more we can do here.
        }
    }
}
