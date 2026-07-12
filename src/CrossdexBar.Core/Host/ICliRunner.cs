namespace CrossdexBar.Core.Host;

public sealed record CliResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs a provider's CLI as a subprocess. Providers depend on this instead of <c>System.Diagnostics.Process</c>
/// directly, so fetch strategies stay testable and every CLI call is timeout-bounded in one place.
/// </summary>
public interface ICliRunner
{
    Task<CliResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct = default);
}
