using Avalonia.Controls;
using Avalonia.Interactivity;
using CrossdexBar.App.ViewModels;

namespace CrossdexBar.App.Views;

public partial class AddInstanceDialog : Window
{
    public AddInstanceDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddInstanceDialogViewModel vm)
        {
            Close(null);
            return;
        }

        Close(new AddInstanceResult(vm.SelectedProvider.Id, vm.Label, vm.BuildSettings()));
    }
}
