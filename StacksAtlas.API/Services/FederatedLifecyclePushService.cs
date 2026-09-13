using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Services;

public sealed class FederatedLifecyclePushService(
    IDeviceRepository deviceRepo,
    IFederationBacklogService backlog,
    FederationSettingsStore settingsStore,
    FederationTelemetryWakeSignal wakeSignal,
    ILogger<FederatedLifecyclePushService> logger) : IFederatedLifecyclePushService
{
    public void PushDeviceState(Guid deviceId)
    {
        if (!ExecutionState.IsFederationActive)
            return;

        var device = deviceRepo.GetById(deviceId);
        if (device == null)
        {
            logger.LogWarning("Lifecycle push skipped: device {DeviceId} not found locally.", deviceId);
            return;
        }

        var settings = settingsStore.Current;
        if (string.IsNullOrWhiteSpace(device.NodeId) && !string.IsNullOrWhiteSpace(settings.NodeId))
            device.NodeId = settings.NodeId;

        if (string.IsNullOrWhiteSpace(device.MacAddress))
        {
            logger.LogWarning(
                "Lifecycle push skipped for device {DeviceId}: MAC address is required for Hub fleet reconciliation.",
                deviceId);
            return;
        }

        device.IsLifecycleGovernancePush = true;

        if (!backlog.TryQueueLifecycleBatch(new List<Device> { device }))
        {
            logger.LogWarning(
                "Lifecycle push for device {DeviceId} failed to queue.",
                deviceId);
            return;
        }

        wakeSignal.Notify();

        logger.LogInformation(
            "Lifecycle push queued for device {DeviceId} Node {NodeId} MAC {Mac} (deleted={Deleted}, permRemoved={PermRemoved}, removedBy={RemovedBy}).",
            deviceId,
            device.NodeId,
            device.MacAddress,
            device.IsDeleted,
            device.IsPermanentlyRemoved,
            device.RemovedBy ?? "(none)");
    }
}
