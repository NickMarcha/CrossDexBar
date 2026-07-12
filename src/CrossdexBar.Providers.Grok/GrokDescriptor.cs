using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Grok;

public static class GrokDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = new()
    {
        Id = "grok",
        DisplayName = "Grok",
        Branding = new ProviderBranding(ColorHex: "#000000", IconResourceName: "ProviderIcon-grok"),
        Capabilities = new ProviderCapabilities(SupportsSecondaryWindow: false),
        InstanceSettingsSchema =
        [
            new ProviderInstanceSettingField(
                Key: "authFilePath",
                Label: "Custom auth.json path (optional)",
                Required: false,
                Placeholder: "Leave blank to use ~/.grok/auth.json"),
        ],
        Strategies = [new GrokAuthFileStrategy()],
    };
}
