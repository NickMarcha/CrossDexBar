using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Copilot;

public static class CopilotDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = new()
    {
        Id = "copilot",
        DisplayName = "GitHub Copilot",
        Branding = new ProviderBranding(ColorHex: "#24292F", IconResourceName: "ProviderIcon-copilot"),
        Capabilities = new ProviderCapabilities(SupportsSecondaryWindow: true),
        Strategies = [new CopilotGhCliStrategy()],
    };
}
