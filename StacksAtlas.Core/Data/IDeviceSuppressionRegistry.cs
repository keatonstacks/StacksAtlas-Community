namespace StacksAtlas.Core.Data;

public interface IDeviceSuppressionRegistry
{
    bool IsSuppressed(string nodeId, string? macAddress);
    void Register(string nodeId, string macAddress, Guid deviceId, string? removedBy);
    void Clear(string nodeId, string macAddress);
}
