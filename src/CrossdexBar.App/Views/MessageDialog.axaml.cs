using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CrossdexBar.App.Views;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private MessageDialog(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message);
        var tcs = new TaskCompletionSource();
        dialog.Closed += (_, _) => tcs.TrySetResult();
        dialog.Show();
        dialog.Activate();
        return tcs.Task;
    }
}
