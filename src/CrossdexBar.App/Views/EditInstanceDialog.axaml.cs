using Avalonia.Controls;
using Avalonia.Interactivity;
using CrossdexBar.App.ViewModels;

namespace CrossdexBar.App.Views;

public partial class EditInstanceDialog : Window
{
    public EditInstanceDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditInstanceDialogViewModel vm)
        {
            Close(null);
            return;
        }

        Close(new EditInstanceResult(vm.Label, vm.BuildSettings()));
    }
}
