using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Licensing;

public class MacHardwareIdProvider : IHardwareIdProvider
{
    private readonly ILogger<MacHardwareIdProvider> _logger;

    public MacHardwareIdProvider(ILogger<MacHardwareIdProvider> logger)
    {
        _logger = logger;
    }

    public string GetHardwareId()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ioreg",
                    Arguments = "-rd1 -c IOPlatformExpertDevice",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            string serial = string.Empty;
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("IOPlatformSerialNumber"))
                {
                    var parts = line.Split('\"');
                    if (parts.Length >= 4)
                    {
                        serial = parts[3];
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(serial))
            {
                throw new Exception("Unable to extract Native Mac Serial Number.");
            }

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(serial));
            return Convert.ToHexString(hashBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Native Mac Hardware ID. Falling back to MachineName hash.");
            using var sha256 = SHA256.Create();
            var fallback = sha256.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName + "_MacFallback"));
            return Convert.ToHexString(fallback);
        }
    }
}
