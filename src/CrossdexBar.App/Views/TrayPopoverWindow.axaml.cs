using Avalonia.Controls;

namespace CrossdexBar.App.Views;

public partial class TrayPopoverWindow : Window
{
    public TrayPopoverWindow()
    {
        InitializeComponent();
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();
}
