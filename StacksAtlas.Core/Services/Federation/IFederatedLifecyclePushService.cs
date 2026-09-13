namespace StacksAtlas.Core.Services.Federation;

/// <summary>
/// Immediately queue a device row for Hub telemetry after local lifecycle mutations on a Node.
/// </summary>
public interface IFederatedLifecyclePushService
{
    void PushDeviceState(Guid deviceId);
}
