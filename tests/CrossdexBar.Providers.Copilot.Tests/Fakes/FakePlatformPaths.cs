using CrossdexBar.Core.Host;

namespace CrossdexBar.Providers.Copilot.Tests.Fakes;

internal sealed class FakePlatformPaths(string homeDirectory) : IPlatformPaths
{
    public string HomeDirectory { get; } = homeDirectory;
    public string ConfigDirectory { get; } = Path.Combine(homeDirectory, "config");
    public string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");
    public string AppDataRoot { get; } = Path.Combine(homeDirectory, "appdata");
}
