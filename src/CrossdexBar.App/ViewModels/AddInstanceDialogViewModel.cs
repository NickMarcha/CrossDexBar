using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.App.ViewModels;

public sealed partial class AddInstanceDialogViewModel : ViewModelBase
{
    public IReadOnlyList<ProviderDescriptor> AvailableProviders { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fields))]
    private ProviderDescriptor _selectedProvider;

    [ObservableProperty] private string _label;

    public ObservableCollection<SettingFieldViewModel> Fields { get; private set; } = new();

    public AddInstanceDialogViewModel(IReadOnlyList<ProviderDescriptor> availableProviders)
    {
        AvailableProviders = availableProviders;
        _selectedProvider = availableProviders[0];
        _label = _selectedProvider.DisplayName;
        RebuildFields();
    }

    partial void OnSelectedProviderChanged(ProviderDescriptor value)
    {
        Label = value.DisplayName;
        RebuildFields();
    }

    private void RebuildFields()
    {
        Fields = new ObservableCollection<SettingFieldViewModel>(
            SelectedProvider.InstanceSettingsSchema.Select(f => new SettingFieldViewModel(f.Key, f.Label, f.Placeholder)));
        OnPropertyChanged(nameof(Fields));
    }

    public IReadOnlyDictionary<string, string> BuildSettings() =>
        Fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToDictionary(f => f.Key, f => f.Value);
}
