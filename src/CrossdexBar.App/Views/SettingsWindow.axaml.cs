using Avalonia.Controls;
using Avalonia.Interactivity;
using CrossdexBar.App.ViewModels;

namespace CrossdexBar.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.SaveCommand.Execute(null);
        Close();
    }

    private void OnRemoveClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProviderInstanceRowViewModel row } && DataContext is SettingsViewModel vm)
            vm.RemoveInstanceCommand.Execute(row);
    }

    private void OnEditClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProviderInstanceRowViewModel row } && DataContext is SettingsViewModel vm)
            _ = vm.EditInstanceCommand.ExecuteAsync(row);
    }
}
