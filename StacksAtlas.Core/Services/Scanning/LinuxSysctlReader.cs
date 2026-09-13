namespace StacksAtlas.Core.Services.Scanning;

public interface ISysctlReader
{
    int? TryReadRpFilter(string interfaceName);
}

public sealed class LinuxSysctlReader : ISysctlReader
{
    public int? TryReadRpFilter(string interfaceName)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        if (string.IsNullOrWhiteSpace(interfaceName))
            return null;

        var sanitized = new string(interfaceName.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
            return null;

        var path = $"/proc/sys/net/ipv4/conf/{sanitized}/rp_filter";
        if (!File.Exists(path))
            return null;

        try
        {
            var raw = File.ReadAllText(path).Trim();
            return int.TryParse(raw, out var value) ? value : null;
        }
        catch
        {
            return null;
        }
    }
}
