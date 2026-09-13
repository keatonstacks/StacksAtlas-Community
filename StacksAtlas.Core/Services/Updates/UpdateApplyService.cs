using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Services.Governance;

namespace StacksAtlas.Core.Services.Updates;

public sealed class UpdateApplyService(
    IUpdateCheckService updateCheckService,
    UpdateArtifactDownloader downloader,
    WindowsUpdateApplyRunner windowsRunner,
    ISnapshotService snapshotService,
    ILogger<UpdateApplyService> logger) : IUpdateApplyService
{
    public async Task<UpdateApplyResult> ApplyAsync(string? channel = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failed(null, channel, "In-appliance apply is supported on Windows in this release.");
        }

        // Apply can take several minutes (manifest + large MSI download). Do not link to the HTTP request token.
        var check = await updateCheckService.CheckForUpdatesAsync(
            channel,
            CancellationToken.None,
            nameof(UpdateApplyService));
        var (applySupported, _) = UpdateApplyCapabilities.Resolve(check.ArtifactKey, check.DownloadUrl);

        if (!check.UpdateAvailable)
        {
            return Failed(
                check.AvailableVersion,
                check.Channel,
                check.Status == UpdateCheckStatuses.UpdateAvailable
                    ? "No update is available to apply."
                    : check.Message ?? "No update is available to apply.");
        }

        if (string.IsNullOrWhiteSpace(check.DownloadUrl) || string.IsNullOrWhiteSpace(check.Sha256))
        {
            return Failed(check.AvailableVersion, check.Channel, "Update metadata is missing a download URL or hash.");
        }

        if (!applySupported)
        {
            return Failed(
                check.AvailableVersion,
                check.Channel,
                "This platform does not support one-click apply yet.");
        }

        CreateSnapshotResult? snapshot = null;
        if (UpdateApplySnapshotPolicy.ShouldCreatePreUpdateSnapshot(check.ArtifactKey))
        {
            try
            {
                snapshot = snapshotService.CreateSnapshot($"PreUpdate_{check.AvailableVersion}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pre-update snapshot failed.");
                return Failed(check.AvailableVersion, check.Channel, "Could not create a pre-update snapshot. Apply was cancelled.");
            }
        }
        else
        {
            logger.LogInformation("Skipping pre-update DB snapshot for portable apply (launcher .pre-update.bak is sufficient).");
        }

        var stagingDir = Path.Combine(PlatformPaths.BaseDataDir, "updates", "staging", check.AvailableVersion ?? "unknown");
        Directory.CreateDirectory(stagingDir);

        var fileName = check.ArtifactKey switch
        {
            UpdateArtifactKeys.WinX64Msi => "StacksAtlas.msi",
            UpdateArtifactKeys.WinX64Portable => "StacksAtlas-Portable.exe",
            _ => "update.bin"
        };
        var stagedPath = Path.Combine(stagingDir, fileName);

        try
        {
            await downloader.DownloadAndVerifyAsync(check.DownloadUrl, check.Sha256, stagedPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Update artifact download failed.");
            return Failed(
                check.AvailableVersion,
                check.Channel,
                ex is InvalidOperationException ? ex.Message : "Download failed. The update was not applied.");
        }

        try
        {
            if (check.ArtifactKey == UpdateArtifactKeys.WinX64Msi)
            {
                var logPath = Path.Combine(stagingDir, "install.log");
                windowsRunner.ScheduleMsiInstall(stagedPath, logPath);
                ScheduleProcessExit();
                return Applying(
                    check.AvailableVersion,
                    check.Channel,
                    snapshot?.ApplianceFileName,
                    snapshot?.FleetFileName,
                    "Update downloaded and verified. Installing MSI  -  the service will restart shortly.");
            }

            if (check.ArtifactKey == UpdateArtifactKeys.WinX64Portable)
            {
                var launcher = Environment.GetEnvironmentVariable(PortableMode.LauncherPathVariable);
                if (string.IsNullOrWhiteSpace(launcher) || !File.Exists(launcher))
                {
                    return Failed(
                        check.AvailableVersion,
                        check.Channel,
                        "Portable launcher path is unavailable. Restart from StacksAtlas-Portable.exe and try again.");
                }

                windowsRunner.SchedulePortableReplace(launcher, stagedPath);
                ScheduleProcessExit();
                return Applying(
                    check.AvailableVersion,
                    check.Channel,
                    snapshot?.ApplianceFileName,
                    snapshot?.FleetFileName,
                    "Update downloaded and verified. Replacing portable launcher  -  the app will restart shortly.");
            }

            return Failed(check.AvailableVersion, check.Channel, "Unsupported artifact type for apply.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to schedule platform apply.");
            return Failed(check.AvailableVersion, check.Channel, "Could not start the update installer.");
        }
    }

    private static void ScheduleProcessExit()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            Environment.Exit(0);
        });
    }

    private static UpdateApplyResult Applying(
        string? targetVersion,
        string? channel,
        string? snapshotAppliance,
        string? snapshotFleet,
        string message) =>
        new(
            Status: UpdateApplyStatuses.Applying,
            Message: message,
            TargetVersion: targetVersion,
            Channel: channel,
            SnapshotApplianceFileName: snapshotAppliance,
            SnapshotFleetFileName: snapshotFleet,
            RestartRequired: true);

    private static UpdateApplyResult Failed(string? targetVersion, string? channel, string message) =>
        new(
            Status: UpdateApplyStatuses.Failed,
            Message: message,
            TargetVersion: targetVersion,
            Channel: channel,
            SnapshotApplianceFileName: null,
            SnapshotFleetFileName: null,
            RestartRequired: false);
}
