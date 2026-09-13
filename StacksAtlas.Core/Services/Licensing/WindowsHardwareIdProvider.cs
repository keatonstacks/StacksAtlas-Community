using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace StacksAtlas.Core.Services.Licensing;

public class WindowsHardwareIdProvider : IHardwareIdProvider
{
    private string? _cachedHwid;

    public string GetHardwareId()
    {
        if (_cachedHwid != null) return _cachedHwid;

        try
        {
            // CRITICAL PERFORMANCE FIX: WMI is extremely slow and causes UI stutters.
            // We switch to reading Registry keys which is near-instant.
            
            // 1. Machine GUID (Persistent across reboots/HW changes, unique to OS install)
            var machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")?.ToString();
            
            // 2. Product ID (Windows License ID)
            var productId = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId", "")?.ToString();

            // 3. Fallback to CPU count if registry is denied
            var processorCount = Environment.ProcessorCount;

            var rawId = $"HWID-V2-{machineGuid}-{productId}-{processorCount}";
            _cachedHwid = HashString(rawId);
            return _cachedHwid;
        }
        catch
        {
            // Fallback for non-standard environments
            _cachedHwid = HashString($"FALLBACK-{Environment.MachineName}-{Environment.ProcessorCount}");
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
