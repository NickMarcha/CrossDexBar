using CrossdexBar.Core.Providers;

namespace CrossdexBar.Core.Tests;

public class ProviderRegistryTests
{
    private static ProviderDescriptor MakeDescriptor(string id) => new()
    {
        Id = id,
        DisplayName = id,
        Branding = new ProviderBranding("#000000", "icon"),
        Strategies = [],
    };

    [Fact]
    public void Register_ThenTryGet_ReturnsDescriptor()
    {
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("codex"));

        var found = registry.TryGet("codex", out var descriptor);

        Assert.True(found);
        Assert.Equal("codex", descriptor.Id);
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        var registry = new ProviderRegistry();
        registry.Register(MakeDescriptor("codex"));

        Assert.Throws<InvalidOperationException>(() => registry.Register(MakeDescriptor("codex")));
    }

    [Fact]
    public void TryGet_UnknownId_ReturnsFalse()
    {
        var registry = new ProviderRegistry();

        Assert.False(registry.TryGet("missing", out _));
    }
}
