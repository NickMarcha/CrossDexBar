using Velopack;
using Velopack.Sources;

namespace CrossdexBar.App.Update;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? Version = null, string? ErrorMessage = null);

/// <summary>
/// Checks GitHub Releases for a newer CrossdexBar build and downloads it. No-ops when not running
/// from a Velopack-installed copy (e.g. `dotnet run` in development, or a pre-Velopack manual install).
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _pendingUpdate;

    public UpdateService(string githubRepoUrl)
    {
        var source = new GithubSource(githubRepoUrl, accessToken: null, prerelease: false);
        _manager = new UpdateManager(source);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled)
            return new UpdateCheckResult(UpdateCheckStatus.Failed, ErrorMessage: "Not running from an installed copy (e.g. a dev build).");

        try
        {
            var updateInfo = await _manager.CheckForUpdatesAsync();
            if (updateInfo is null)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);

            await _manager.DownloadUpdatesAsync(updateInfo, cancelToken: ct);
            _pendingUpdate = updateInfo;
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, updateInfo.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, ErrorMessage: ex.Message);
        }
    }

    /// <summary>Applies the update downloaded by the last <see cref="CheckForUpdatesAsync"/> call and restarts. No-ops if there is none.</summary>
    public void ApplyPendingUpdateAndRestart()
    {
        if (_pendingUpdate is not null)
            _manager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}
