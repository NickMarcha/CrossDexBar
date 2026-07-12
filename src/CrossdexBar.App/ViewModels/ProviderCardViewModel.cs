using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossdexBar.Core.Providers;
using CrossdexBar.Core.Refresh;

namespace CrossdexBar.App.ViewModels;

public sealed partial class ProviderCardViewModel : ViewModelBase
{
    private readonly ProviderInstance _instance;
    private readonly RefreshService _refreshService;
    private readonly ResetDisplayMode _resetDisplayMode;
    private DateTimeOffset? _primaryResetsAt;
    private DateTimeOffset? _secondaryResetsAt;

    public Guid InstanceId { get; }
    public string Label { get; }
    public IBrush BrandBrush { get; }

    [ObservableProperty] private double _primaryPercent;
    [ObservableProperty] private string _primaryPercentText = "";
    [ObservableProperty] private string _primaryLabel = "Session";
    [ObservableProperty] private string _primaryResetText = "";
    [ObservableProperty] private bool _hasSecondary;
    [ObservableProperty] private double _secondaryPercent;
    [ObservableProperty] private string _secondaryPercentText = "";
    [ObservableProperty] private string _secondaryLabel = "Weekly";
    [ObservableProperty] private string _secondaryResetText = "";
    [ObservableProperty] private string _statusMessage = "Not refreshed yet";
    [ObservableProperty] private IBrush _statusBrush = Brushes.Gray;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _lastUpdatedText = "Never refreshed";

    public IAsyncRelayCommand RefreshCommand { get; }

    public ProviderCardViewModel(
        ProviderInstance instance, ProviderDescriptor descriptor, RefreshService refreshService, ResetDisplayMode resetDisplayMode)
    {
        _instance = instance;
        _refreshService = refreshService;
        _resetDisplayMode = resetDisplayMode;
        InstanceId = instance.InstanceId;
        Label = instance.Label;
        BrandBrush = Brush.Parse(descriptor.Branding.ColorHex);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Refreshing...";
        StatusBrush = Brushes.Gray;
        IsConnected = false;
        try
        {
            var outcome = await _refreshService.RefreshInstanceAsync(_instance);
            Apply(outcome);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Apply(ProviderFetchOutcome outcome)
    {
        switch (outcome)
        {
            case ProviderFetchOutcome.Success success:
                StatusMessage = "Connected";
                StatusBrush = Brushes.SeaGreen;
                IsConnected = true;
                PrimaryPercent = success.Snapshot.Primary.UsedPercent;
                PrimaryPercentText = $"{success.Snapshot.Primary.UsedPercent:0}% used";
                PrimaryLabel = success.Snapshot.Primary.Label;
                _primaryResetsAt = success.Snapshot.Primary.ResetsAt;
                HasSecondary = success.Snapshot.Secondary is not null;
                if (success.Snapshot.Secondary is { } secondary)
                {
                    SecondaryPercent = secondary.UsedPercent;
                    SecondaryPercentText = $"{secondary.UsedPercent:0}% used";
                    SecondaryLabel = secondary.Label;
                    _secondaryResetsAt = secondary.ResetsAt;
                }
                else
                {
                    _secondaryResetsAt = null;
                }

                RefreshResetDisplay();
                LastUpdatedText = $"Updated {success.Snapshot.UpdatedAt.ToLocalTime():t}";
                break;

            case ProviderFetchOutcome.NotSignedIn notSignedIn:
                StatusMessage = notSignedIn.Message;
                StatusBrush = Brushes.Orange;
                break;

            case ProviderFetchOutcome.Unavailable unavailable:
                StatusMessage = unavailable.Message;
                StatusBrush = Brushes.SteelBlue;
                break;

            case ProviderFetchOutcome.Failure failure:
                StatusMessage = failure.Message;
                StatusBrush = Brushes.OrangeRed;
                break;
        }
    }

    /// <summary>Recomputes the reset text from the already-fetched timestamps — no new fetch needed when the user flips the display-mode toggle.</summary>
    public void RefreshResetDisplay()
    {
        PrimaryResetText = FormatReset(_primaryResetsAt, _resetDisplayMode.ShowAbsolute);
        SecondaryResetText = FormatReset(_secondaryResetsAt, _resetDisplayMode.ShowAbsolute);
    }

    private static string FormatReset(DateTimeOffset? resetsAt, bool showAbsolute)
    {
        if (resetsAt is not { } at)
            return "";

        if (showAbsolute)
            return $"Resets {at.ToLocalTime():g}";

        var delta = at - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero)
            return "Resetting...";

        if (delta.TotalDays >= 1)
            return $"Resets in {(int)delta.TotalDays}d {delta.Hours}h";

        return delta.TotalHours >= 1
            ? $"Resets in {(int)delta.TotalHours}h {delta.Minutes}m"
            : $"Resets in {(int)delta.TotalMinutes}m";
    }
}
