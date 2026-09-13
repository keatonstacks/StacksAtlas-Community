namespace StacksAtlas.Core.Models;

/// <summary>
/// Defines the types of alert events supported by the StacksAtlas engine.
/// </summary>
public enum AlertEventType
{
    Unknown,
    DeviceDown,
    DeviceUp,
    NewDeviceDiscovered,
    NodeDecoupled,
    SiteResetInitiated,
    NodeIdentityReconciled
}
