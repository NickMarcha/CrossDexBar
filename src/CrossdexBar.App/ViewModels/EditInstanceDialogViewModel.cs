using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.App.ViewModels;

public sealed partial class EditInstanceDialogViewModel : ViewModelBase
{
    public string ProviderDisplayName { get; }

    [ObservableProperty] private string _label;

    public ObservableCollection<SettingFieldViewModel> Fields { get; }

    public EditInstanceDialogViewModel(ProviderDescriptor descriptor, ProviderInstance instance)
    {
        ProviderDisplayName = descriptor.DisplayName;
        _label = instance.Label;
        Fields = new ObservableCollection<SettingFieldViewModel>(
            descriptor.InstanceSettingsSchema.Select(f =>
                new SettingFieldViewModel(f.Key, f.Label, f.Placeholder) { Value = instance.GetSetting(f.Key) ?? "" }));
    }

    public IReadOnlyDictionary<string, string> BuildSettings() =>
        Fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToDictionary(f => f.Key, f => f.Value);
}
