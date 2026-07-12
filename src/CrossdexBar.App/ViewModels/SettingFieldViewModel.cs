using CommunityToolkit.Mvvm.ComponentModel;

namespace CrossdexBar.App.ViewModels;

public sealed partial class SettingFieldViewModel(string key, string label, string? placeholder) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string? Placeholder { get; } = placeholder;

    [ObservableProperty] private string _value = "";
}
