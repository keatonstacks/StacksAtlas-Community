using System.Security.Cryptography;
using System.Text;

namespace StacksAtlas.Core.Services.Licensing;

public class LinuxHardwareIdProvider : IHardwareIdProvider
{
    private string? _cachedHwid;

    public string GetHardwareId()
    {
        if (_cachedHwid != null) return _cachedHwid;

        try
        {
            // Primary unique identifier for Linux systems
            if (File.Exists("/etc/machine-id"))
            {
                var machineId = File.ReadAllText("/etc/machine-id").Trim();
                var processorCount = Environment.ProcessorCount;
                var rawId = $"HWID-LINUX-{machineId}-{processorCount}";
                _cachedHwid = HashString(rawId);
                return _cachedHwid;
            }
            
            throw new Exception("Cannot find /etc/machine-id");
        }
        catch
        {
            // Fallback for non-standard Linux environments
            _cachedHwid = HashString($"FALLBACK-LINUX-{Environment.MachineName}-{Environment.ProcessorCount}");
            return _cachedHwid;
        }
    }

    private static string HashString(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
