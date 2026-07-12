using CrossdexBar.Core.Host;

namespace CrossdexBar.Core.Tests.Fakes;

internal sealed class FakeConfigStore : IConfigStore
{
    private AppConfig _config = new();

    public AppConfig Load() => _config;

    public void Save(AppConfig config) => _config = config;
}
