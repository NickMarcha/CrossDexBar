using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Cursor;

public static class CursorDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = new()
    {
        Id = "cursor",
        DisplayName = "Cursor",
        Branding = new ProviderBranding(ColorHex: "#00BFA5", IconResourceName: "ProviderIcon-cursor"),
        Capabilities = new ProviderCapabilities(SupportsSecondaryWindow: false),
        InstanceSettingsSchema =
        [
            new ProviderInstanceSettingField(
                Key: "vscdbPath",
                Label: "Custom state.vscdb path (optional)",
                Required: false,
                Placeholder: "Leave blank to use Cursor's default local session file"),
        ],
        Strategies = [new CursorAppAuthStrategy()],
    };
}
