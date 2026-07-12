using CrossdexBar.Core.Host;

namespace CrossdexBar.Core.Providers;

/// <summary>
/// Everything a fetch strategy needs, scoped to one instance. Strategies never touch the filesystem,
/// network, or process table directly — only through these narrow host APIs — so they stay portable
/// and mockable in tests.
/// </summary>
public sealed class ProviderFetchContext
{
    public required ProviderInstance Instance { get; init; }
    public required ICliRunner CliRunner { get; init; }
    public required IHttpApi HttpApi { get; init; }
    public required IConfigStore ConfigStore { get; init; }
    public required IPlatformPaths PlatformPaths { get; init; }

    public string? GetSetting(string key) => Instance.GetSetting(key);
}
