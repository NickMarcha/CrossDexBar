using Velopack;
using Velopack.Sources;

namespace CrossdexBar.App.Update;

/// <summary>
/// Checks GitHub Releases for a newer CrossdexBar build and applies it. No-ops when not running
/// from a Velopack-installed copy (e.g. `dotnet run` in development, or a pre-Velopack manual install).
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager _manager;

    public UpdateService(string githubRepoUrl)
    {
        var source = new GithubSource(githubRepoUrl, accessToken: null, prerelease: false);
        _manager = new UpdateManager(source);
    }

    public async Task CheckAndApplyAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled)
            return;

        var updateInfo = await _manager.CheckForUpdatesAsync();
        if (updateInfo is null)
            return;

        await _manager.DownloadUpdatesAsync(updateInfo, cancelToken: ct);
        _manager.ApplyUpdatesAndRestart(updateInfo);
    }
}
