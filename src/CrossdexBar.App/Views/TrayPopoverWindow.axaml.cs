using Avalonia.Controls;
using Avalonia.Input;
using CrossdexBar.App.ViewModels;

namespace CrossdexBar.App.Views;

public partial class TrayPopoverWindow : Window
{
    // Set once the user drags the window; suppresses the automatic bottom-right re-anchoring in
    // App.TogglePopover so a dragged position survives hide/show, until "Reset window position" clears it.
    public bool HasCustomPosition { get; private set; }

    public TrayPopoverWindow()
    {
        InitializeComponent();
    }

    public void ResetCustomPosition() => HasCustomPosition = false;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (DataContext is TrayPopoverViewModel { IsPinned: true })
            return;

        Hide();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        HasCustomPosition = true;
        BeginMoveDrag(e);
    }
}
