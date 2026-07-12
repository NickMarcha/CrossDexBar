using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Claude;

public static class ClaudeDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = new()
    {
        Id = "claude",
        DisplayName = "Claude",
        Branding = new ProviderBranding(ColorHex: "#D97757", IconResourceName: "ProviderIcon-claude"),
        Capabilities = new ProviderCapabilities(SupportsSecondaryWindow: true),
        InstanceSettingsSchema =
        [
            new ProviderInstanceSettingField(
                Key: "credentialsFilePath",
                Label: "Custom credentials file path (optional)",
                Required: false,
                Placeholder: "Leave blank to use ~/.claude/.credentials.json"),
        ],
        Strategies = [new ClaudeAuthFileStrategy()],
    };
}
