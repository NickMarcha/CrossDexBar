using CrossdexBar.Core.Host;

namespace CrossdexBar.Providers.Copilot.Tests.Fakes;

internal sealed class FakeCliRunner(Func<string, IReadOnlyList<string>, CliResult> run) : ICliRunner
{
    public Task<CliResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct = default) =>
        Task.FromResult(run(fileName, arguments));
}
