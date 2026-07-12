using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Codex;

public static class CodexDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = new()
    {
        Id = "codex",
        DisplayName = "Codex",
        Branding = new ProviderBranding(ColorHex: "#10A37F", IconResourceName: "ProviderIcon-codex"),
        Capabilities = new ProviderCapabilities(SupportsSecondaryWindow: true),
        InstanceSettingsSchema =
        [
            new ProviderInstanceSettingField(
                Key: "authFilePath",
                Label: "Custom auth.json path (optional)",
                Required: false,
                Placeholder: "Leave blank to use ~/.codex/auth.json"),
        ],
        Strategies = [new CodexAuthFileStrategy()],
    };
}
