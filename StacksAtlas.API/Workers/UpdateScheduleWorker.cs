using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Services.Audit;
using StacksAtlas.Core.Services.Updates;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Workers;

/// <summary>
/// Appliance-local scheduled update apply (Slice 5). Evaluates the maintenance window without UI open.
/// Portable and Hub-brain appliances never auto-apply from this worker.
/// </summary>
public sealed class UpdateScheduleWorker(
    ILogger<UpdateScheduleWorker> logger,
    SystemSettingsStore systemSettings,
    IUpdateCheckService updateCheckService,
    IUpdateApplyService updateApplyService,
    IApplianceVersionProvider versionProvider,
    IAuditService audit,
    IClock clock,
    SystemStateProvider stateProvider) : BackgroundService
{
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("UpdateScheduleWorker is active.");

        while (!stateProvider.IsDatabaseReady && !stoppingToken.IsCancellationRequested)
            await Task.Delay(500, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UpdateScheduleWorker evaluation failed.");
            }

            await DelayUnlessSettingsChanged(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        if (ExecutionState.IsPortable || ExecutionState.IsHubBrainEnabled)
            return;

        if (!OperatingSystem.IsWindows())
            return;

        var settings = systemSettings.Load();
        var schedule = UpdateFleetPolicy.NormalizeSchedule(settings.UpdateSchedule);
        if (!schedule.Enabled)
            return;

        var current = versionProvider.GetCurrent();
        if (current.IsPortable)
            return;

        if (!UpdateFleetPolicy.IsInMaintenanceWindow(schedule, clock.Now))
            return;

        if (!await _applyGate.WaitAsync(0, cancellationToken))
            return;

        try
        {
            var channel = string.IsNullOrWhiteSpace(current.Channel) ? "stable" : current.Channel;
            var check = await updateCheckService.CheckForUpdatesAsync(channel, cancellationToken);
            if (!check.UpdateAvailable || string.IsNullOrWhiteSpace(check.AvailableVersion))
                return;

            if (!UpdateFleetPolicy.ShouldAutoApply(schedule, check.AvailableVersion, clock.Now, current.IsPortable))
                return;

            logger.LogWarning(
                "Scheduled apply starting for v{Version} (channel={Channel})",
                check.AvailableVersion,
                check.Channel);

            audit.Record(
                AuditActions.UpdatesScheduleApply,
                "system_update",
                check.AvailableVersion,
                AuditOutcomes.Success,
                detail: $"phase=start; channel={check.Channel}",
                actorUsernameOverride: "scheduled-apply");

            var result = await updateApplyService.ApplyAsync(check.Channel, CancellationToken.None);
            if (string.Equals(result.Status, UpdateApplyStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                audit.Record(
                    AuditActions.UpdatesScheduleApply,
                    "system_update",
                    check.AvailableVersion,
                    AuditOutcomes.Failed,
                    detail: $"phase=apply; message={result.Message}",
                    actorUsernameOverride: "scheduled-apply");
                return;
            }

            var next = systemSettings.Load();
            next.UpdateSchedule = UpdateFleetPolicy.NormalizeSchedule(next.UpdateSchedule);
            next.UpdateSchedule.LastAppliedVersion = check.AvailableVersion;
            next.PendingHubUpdate = new PendingHubUpdateSettings();
            systemSettings.Save(next);

            audit.Record(
                AuditActions.UpdatesScheduleApply,
                "system_update",
                check.AvailableVersion,
                AuditOutcomes.Success,
                detail: $"phase=apply; channel={check.Channel}",
                actorUsernameOverride: "scheduled-apply");
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async Task DelayUnlessSettingsChanged(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var wake = new CancellationTokenSource();
        void OnChanged() => wake.Cancel();
        systemSettings.OnSettingsChanged += OnChanged;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, wake.Token);
            await Task.Delay(delay, linked.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Settings changed  -  re-evaluate promptly.
        }
        finally
        {
            systemSettings.OnSettingsChanged -= OnChanged;
        }
    }
}
