using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Helpers;

namespace StacksAtlas.Core.Services.Security;

/// <summary>
/// Installs the StacksAtlas Local CA into the current user's Trusted Root store.
/// Browsers need the issuing CA trusted (mkcert-style), not the TLS leaf certificate.
/// </summary>
public static class ApplianceTlsTrust
{
    public const string CaFileName = CertificateManager.CaFileName;
    public const string LeafFileName = CertificateManager.LeafFileName;
    public const string PfxPassword = CertificateManager.PfxPassword;

    /// <summary>CA PFX  -  use for trust-store operations.</summary>
    public const string CertFileName = CaFileName;

    public static string GetCaPfxPath(string baseDataDir) =>
        Path.Combine(baseDataDir, "certs", CaFileName);

    public static string GetLeafPfxPath(string baseDataDir) =>
        Path.Combine(baseDataDir, "certs", LeafFileName);

    public static string GetAppliancePfxPath(string baseDataDir) => GetCaPfxPath(baseDataDir);

    public static async Task<bool> WaitAndTrustApplianceCertificateAsync(
        string caPfxPath,
        TimeSpan timeout,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            log?.Invoke("TLS auto-trust skipped (non-Windows).");
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(caPfxPath))
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            if (IsTrustedInCurrentUserStore(caPfxPath))
            {
                log?.Invoke("StacksAtlas Local CA already in CurrentUser Trusted Root.");
                return true;
            }

            TryTrustApplianceCertificate(caPfxPath, log);
            break;
        }

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsTrustedInCurrentUserStore(caPfxPath))
            {
                log?.Invoke("StacksAtlas Local CA installed to CurrentUser Trusted Root.");
                return true;
            }

            await Task.Delay(500, cancellationToken);
        }

        log?.Invoke($"TLS auto-trust timed out waiting for {caPfxPath}.");
        return false;
    }

    public static async Task<bool> WaitForTrustedInCurrentUserStoreAsync(
        string caPfxPath,
        TimeSpan timeout,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(caPfxPath) && IsTrustedInCurrentUserStore(caPfxPath))
            {
                log?.Invoke("StacksAtlas Local CA detected in CurrentUser Trusted Root.");
                return true;
            }

            await Task.Delay(500, cancellationToken);
        }

        log?.Invoke($"TLS trust wait timed out for {caPfxPath}.");
        return false;
    }

    public static bool TryTrustApplianceCertificate(string caPfxPath, Action<string>? log = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (!File.Exists(caPfxPath))
        {
            log?.Invoke($"StacksAtlas Local CA not found at {caPfxPath}.");
            return false;
        }

        try
        {
            using var pfx = X509CertificateLoader.LoadPkcs12FromFile(
                caPfxPath,
                PfxPassword,
                X509KeyStorageFlags.EphemeralKeySet);

            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            var existing = store.Certificates.Find(X509FindType.FindByThumbprint, pfx.Thumbprint, validOnly: false);
            if (existing.Count > 0)
            {
                log?.Invoke("StacksAtlas Local CA already in CurrentUser Trusted Root.");
                return true;
            }

            var publicCert = X509CertificateLoader.LoadCertificate(pfx.Export(X509ContentType.Cert));
            store.Add(publicCert);
            log?.Invoke("StacksAtlas Local CA installed to CurrentUser Trusted Root.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Appliance TLS auto-trust failed: {ex.Message}");
            return false;
        }
    }

    public const string TrustScheduledTaskName = "StacksAtlas.TrustLocalCa";

    public static bool IsTrustScheduledTaskRegistered()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TrustScheduledTaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            return process != null && process.WaitForExit(3000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySpawnWindowsTrustHelper(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (TrySpawnPortableTrustHelper(logger))
            return true;

        if (!IsTrustScheduledTaskRegistered())
        {
            logger?.LogDebug("Windows trust task not registered  -  one-click trust unavailable.");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Run /TN \"{TrustScheduledTaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            logger?.LogInformation("Started Windows TLS trust scheduled task {Task}", TrustScheduledTaskName);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not run Windows TLS trust scheduled task.");
            return false;
        }
    }

    public static bool CanOneClickTrustOnWindows(bool certificateReady, bool caPresent) =>
        OperatingSystem.IsWindows() && certificateReady && caPresent &&
        (PortableMode.IsEnabled || IsTrustScheduledTaskRegistered());

    public static bool TrySpawnPortableTrustHelper(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows() || !PortableMode.IsEnabled)
            return false;

        var launcher = Environment.GetEnvironmentVariable(PortableMode.LauncherPathVariable);
        if (string.IsNullOrWhiteSpace(launcher) || !File.Exists(launcher))
        {
            logger?.LogDebug("Portable trust helper not spawned  -  launcher path unavailable.");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                Arguments = "--trust-cert",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            logger?.LogInformation("Spawned portable TLS trust helper at {Launcher}", launcher);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not spawn portable TLS trust helper.");
            return false;
        }
    }

    public static bool IsTrustedInCurrentUserStore(string caPfxPath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(caPfxPath))
            return false;

        try
        {
            using var pfx = X509CertificateLoader.LoadPkcs12FromFile(
                caPfxPath,
                PfxPassword,
                X509KeyStorageFlags.EphemeralKeySet);
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, pfx.Thumbprint, validOnly: false).Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
