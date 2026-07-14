using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using CrossdexBar.App.Update;
using CrossdexBar.App.ViewModels;
using CrossdexBar.App.Views;
using CrossdexBar.Core.Host;
using CrossdexBar.Core.Providers;
using CrossdexBar.Core.Refresh;
using CrossdexBar.Providers.Claude;
using CrossdexBar.Providers.Copilot;
using CrossdexBar.Providers.Codex;
using CrossdexBar.Providers.Cursor;
using CrossdexBar.Providers.Grok;
using CrossdexBar.Providers.Ollama;

namespace CrossdexBar.App;

public partial class App : Application
{
    private IPlatformPaths _platformPaths = null!;
    private ProviderRegistry _registry = null!;
    private InstanceStore _instanceStore = null!;
    private RefreshService _refreshService = null!;
    private TrayPopoverViewModel _popoverViewModel = null!;
    private TrayPopoverWindow? _popoverWindow;
    private UpdateService _updateService = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tray-only app: no main window, so the app must be told explicitly when to shut down.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ComposeServices();
            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ComposeServices()
    {
        _platformPaths = new PlatformPaths();
        var configStore = new JsonConfigStore(_platformPaths);

        _registry = new ProviderRegistry();
        _registry.Register(CodexDescriptor.Descriptor);
        _registry.Register(ClaudeDescriptor.Descriptor);
        _registry.Register(CopilotDescriptor.Descriptor);
        _registry.Register(CursorDescriptor.Descriptor);
        _registry.Register(GrokDescriptor.Descriptor);
        _registry.Register(OllamaDescriptor.Descriptor);

        _instanceStore = new InstanceStore(configStore);
        _instanceStore.Load();
        var seededDefault = SeedDefaultInstanceIfMissing("codex", "Codex")
            | SeedDefaultInstanceIfMissing("claude", "Claude")
            | SeedDefaultInstanceIfMissing("copilot", "Copilot")
            | SeedDefaultInstanceIfMissing("cursor", "Cursor")
            | SeedDefaultInstanceIfMissing("grok", "Grok")
            | SeedDefaultInstanceIfMissing("ollama", "Ollama");
        if (seededDefault)
            _instanceStore.Save();

        _refreshService = new RefreshService(_registry, new ProcessCliRunner(), new HttpApi(), configStore, _platformPaths);
        _updateService = new UpdateService("https://github.com/NickMarcha/CrossDexBar");

        _popoverViewModel = new TrayPopoverViewModel(_refreshService, OpenSettings, Quit);
        RebuildCards();

        _refreshService.Start(TimeSpan.FromSeconds(_instanceStore.RefreshIntervalSeconds), () => _instanceStore.Instances);
    }

    private bool SeedDefaultInstanceIfMissing(string providerId, string label)
    {
        if (_instanceStore.Instances.Any(i => i.ProviderId == providerId))
            return false;

        _instanceStore.Add(providerId, label, new Dictionary<string, string>());
        return true;
    }

    private void RebuildCards()
    {
        var enabled = new List<(ProviderInstance Instance, ProviderDescriptor Descriptor)>();
        foreach (var instance in _instanceStore.Instances)
        {
            if (instance.Enabled && _registry.TryGet(instance.ProviderId, out var descriptor))
                enabled.Add((instance, descriptor));
        }

        _popoverViewModel.SetInstances(enabled);
    }

    private void SetupTrayIcon()
    {
        var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CrossdexBar.App/Assets/avalonia-logo.ico")));

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (_, _) => OpenSettings();
        var checkForUpdatesItem = new NativeMenuItem("Check for updates...");
        checkForUpdatesItem.Click += (_, _) => _ = _updateService.CheckAndApplyAsync();
        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();
        var menu = new NativeMenu { settingsItem, checkForUpdatesItem, quitItem };

        var trayIcon = new TrayIcon { Icon = icon, ToolTipText = "CrossdexBar", Menu = menu };
        trayIcon.Clicked += (_, _) => TogglePopover();

        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
    }

    private void TogglePopover()
    {
        if (_popoverWindow is { IsVisible: true })
        {
            _popoverWindow.Hide();
            return;
        }

        if (_popoverWindow is null)
        {
            _popoverWindow = new TrayPopoverWindow { DataContext = _popoverViewModel };
            // SizeToContent="Height" only knows the final height after layout runs (i.e. after Show()),
            // so the precise bottom-right anchor is (re)computed once more from Opened.
            _popoverWindow.Opened += (_, _) => PositionPopover(_popoverWindow);
        }

        ConstrainPopoverHeightToScreen(_popoverWindow);
        PositionPopover(_popoverWindow);
        _popoverWindow.Show();
        _popoverWindow.Activate();
        _ = _popoverViewModel.RefreshAllCommand.ExecuteAsync(null);
    }

    private static void ConstrainPopoverHeightToScreen(Window window)
    {
        var screen = window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
        if (screen is null)
            return;

        const int marginDip = 24; // top + bottom margin combined
        window.MaxHeight = Math.Max(200, screen.WorkingArea.Height / screen.Scaling - marginDip);
    }

    private static void PositionPopover(Window window)
    {
        var screen = window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
        if (screen is null)
            return;

        // WorkingArea is in physical pixels but Width/Height are DIPs, so on a scaled display (125%/150%/...)
        // subtracting the raw DIP size leaves the window partly off-screen. Convert to physical pixels first.
        // With SizeToContent="Height", the real height (Bounds.Height) is only known once layout has run
        // (i.e. after Show()/Opened) — before that, MaxHeight is used as a same-ballpark estimate.
        var workArea = screen.WorkingArea;
        var heightDip = window.Bounds.Height > 0 ? window.Bounds.Height : window.MaxHeight;
        if (double.IsNaN(heightDip) || double.IsInfinity(heightDip) || heightDip <= 0)
            heightDip = 400;

        var widthPx = (int)(window.Width * screen.Scaling);
        var heightPx = (int)(heightDip * screen.Scaling);
        var marginPx = (int)(12 * screen.Scaling);

        var x = Math.Max(workArea.X, workArea.X + workArea.Width - widthPx - marginPx);
        var y = Math.Max(workArea.Y, workArea.Y + workArea.Height - heightPx - marginPx);
        window.Position = new PixelPoint(x, y);
    }

    private void OpenSettings()
    {
        SettingsWindow? window = null;
        var settingsViewModel = new SettingsViewModel(
            _instanceStore,
            _registry,
            _platformPaths,
            providers => ShowAddInstanceDialogAsync(providers, window!),
            (descriptor, instance) => ShowEditInstanceDialogAsync(descriptor, instance, window!),
            RebuildCards);
        window = new SettingsWindow { DataContext = settingsViewModel };
        window.Show();
        window.Activate();
    }

    private static async Task<AddInstanceResult?> ShowAddInstanceDialogAsync(IReadOnlyList<ProviderDescriptor> providers, Window owner)
    {
        var dialog = new AddInstanceDialog { DataContext = new AddInstanceDialogViewModel(providers) };
        return await dialog.ShowDialog<AddInstanceResult?>(owner);
    }

    private static async Task<EditInstanceResult?> ShowEditInstanceDialogAsync(ProviderDescriptor descriptor, ProviderInstance instance, Window owner)
    {
        var dialog = new EditInstanceDialog { DataContext = new EditInstanceDialogViewModel(descriptor, instance) };
        return await dialog.ShowDialog<EditInstanceResult?>(owner);
    }

    private void Quit()
    {
        _refreshService.Stop();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
