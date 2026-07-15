using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossdexBar.Core.Providers;
using CrossdexBar.Core.Refresh;

namespace CrossdexBar.App.ViewModels;

public sealed partial class TrayPopoverViewModel : ViewModelBase
{
    private readonly RefreshService _refreshService;
    private readonly ResetDisplayMode _resetDisplayMode = new();

    public ObservableCollection<ProviderCardViewModel> Cards { get; } = new();

    [ObservableProperty] private string _resetModeGlyph = "⏱";
    [ObservableProperty] private string _resetModeTooltip = "Showing time left until reset — click for reset date";
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string _pinTooltip = "Pin window (stay open and on top)";

    public IAsyncRelayCommand RefreshAllCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand QuitCommand { get; }
    public IRelayCommand ToggleResetModeCommand { get; }
    public IRelayCommand TogglePinCommand { get; }

    public TrayPopoverViewModel(RefreshService refreshService, Action openSettings, Action quit)
    {
        _refreshService = refreshService;
        _refreshService.InstanceRefreshed += OnInstanceRefreshed;
        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync);
        OpenSettingsCommand = new RelayCommand(openSettings);
        QuitCommand = new RelayCommand(quit);
        ToggleResetModeCommand = new RelayCommand(ToggleResetMode);
        TogglePinCommand = new RelayCommand(TogglePin);
    }

    private void TogglePin()
    {
        IsPinned = !IsPinned;
        PinTooltip = IsPinned ? "Unpin window" : "Pin window (stay open and on top)";
    }

    public void SetInstances(IEnumerable<(ProviderInstance Instance, ProviderDescriptor Descriptor)> instances)
    {
        Cards.Clear();
        foreach (var (instance, descriptor) in instances)
        {
            var card = new ProviderCardViewModel(instance, descriptor, _refreshService, _resetDisplayMode);
            if (_refreshService.GetLatest(instance.InstanceId) is { } cached)
                card.Apply(cached);
            Cards.Add(card);
        }
    }

    private async Task RefreshAllAsync()
    {
        await Task.WhenAll(Cards.Select(c => c.RefreshAsync()));
    }

    private void ToggleResetMode()
    {
        _resetDisplayMode.ShowAbsolute = !_resetDisplayMode.ShowAbsolute;
        ResetModeGlyph = _resetDisplayMode.ShowAbsolute ? "🕐" : "⏱";
        ResetModeTooltip = _resetDisplayMode.ShowAbsolute
            ? "Showing reset date — click for time left"
            : "Showing time left until reset — click for reset date";

        foreach (var card in Cards)
            card.RefreshResetDisplay();
    }

    private void OnInstanceRefreshed(object? sender, InstanceRefreshedEventArgs e)
    {
        // RefreshService's background loop fires this off the UI thread; card properties are bound to the UI.
        Dispatcher.UIThread.Post(() =>
        {
            var card = Cards.FirstOrDefault(c => c.InstanceId == e.InstanceId);
            card?.Apply(e.Outcome);
        });
    }
}
