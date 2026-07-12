using CrossdexBar.Core.Providers;
using CrossdexBar.Core.Tests.Fakes;

namespace CrossdexBar.Core.Tests;

public class InstanceStoreTests
{
    [Fact]
    public void AddThenSaveThenLoad_RoundTripsInstance()
    {
        var configStore = new FakeConfigStore();
        var store = new InstanceStore(configStore);
        store.Load();

        var added = store.Add("codex", "Codex (work)", new Dictionary<string, string> { ["authFilePath"] = @"C:\creds\auth.json" });
        store.RefreshIntervalSeconds = 60;
        store.Save();

        var reloaded = new InstanceStore(configStore);
        reloaded.Load();

        var instance = Assert.Single(reloaded.Instances);
        Assert.Equal(added.InstanceId, instance.InstanceId);
        Assert.Equal("codex", instance.ProviderId);
        Assert.Equal("Codex (work)", instance.Label);
        Assert.Equal(@"C:\creds\auth.json", instance.GetSetting("authFilePath"));
        Assert.Equal(60, reloaded.RefreshIntervalSeconds);
    }

    [Fact]
    public void Add_AllowsMultipleInstancesOfSameProvider()
    {
        var store = new InstanceStore(new FakeConfigStore());
        store.Add("codex", "Codex (work)", new Dictionary<string, string>());
        store.Add("codex", "Codex (personal)", new Dictionary<string, string>());

        Assert.Equal(2, store.Instances.Count);
        Assert.All(store.Instances, i => Assert.Equal("codex", i.ProviderId));
    }

    [Fact]
    public void Remove_RemovesOnlyMatchingInstance()
    {
        var store = new InstanceStore(new FakeConfigStore());
        var keep = store.Add("codex", "Keep", new Dictionary<string, string>());
        var remove = store.Add("codex", "Remove", new Dictionary<string, string>());

        store.Remove(remove.InstanceId);

        var remaining = Assert.Single(store.Instances);
        Assert.Equal(keep.InstanceId, remaining.InstanceId);
    }
}
