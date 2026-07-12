using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Ollama;

public static class OllamaDescriptor
{
    public static ProviderDescriptor Descriptor { get; } = new()
    {
        Id = "ollama",
        DisplayName = "Ollama",
        Branding = new ProviderBranding(ColorHex: "#FFFFFF", IconResourceName: "ProviderIcon-ollama"),
        Capabilities = new ProviderCapabilities(SupportsSecondaryWindow: false),
        InstanceSettingsSchema =
        [
            new ProviderInstanceSettingField(
                Key: "cookieHeader",
                Label: "Cookie header (optional, for real usage bars)",
                Required: false,
                Placeholder: "From ollama.com/settings — DevTools → Network tab → copy the Cookie request header"),
            new ProviderInstanceSettingField(
                Key: "apiKey",
                Label: "Ollama API key",
                Required: false,
                Placeholder: "From https://ollama.com/settings/keys, or leave blank to use OLLAMA_API_KEY"),
        ],
        Strategies = [new OllamaWebUsageStrategy(), new OllamaApiKeyStrategy()],
    };
}
