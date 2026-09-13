using System;
using System.IO;
using System.Runtime.InteropServices;

namespace StacksAtlas.Core.Helpers;

/// <summary>
/// Provides thread-safe, cross-platform path resolution for StacksAtlas data and configuration.
/// </summary>
public static class PlatformPaths
{
    private static readonly object _lock = new();
    private static string? _baseDataDir;

    /// <summary>
    /// Gets the root directory where all StacksAtlas data is stored.
    /// Priority: Environment Variable > OS Default > Fallback
    /// </summary>
    public static string BaseDataDir
    {
        get
        {
            // Double-checked locking for high-concurrency startup safety
            if (_baseDataDir != null) return _baseDataDir;

            lock (_lock)
            {
                if (_baseDataDir != null) return _baseDataDir;

                _baseDataDir = ResolveBaseDataDir();
                return _baseDataDir;
            }
        }
    }

    private static string ResolveBaseDataDir()
    {
        // 1. Check for Environment Override (Critical for Docker/Containerized environments)
        var envDir = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        if (!string.IsNullOrEmpty(envDir)) return envDir;

        // 2. Portable evaluation  -  user-writable trial path (never system dirs)
        if (PortableMode.IsEnabled)
            return EnsureWritableDirectory(PortableMode.DefaultTrialDataDirectory());

        var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        // 3. Platform-Specific Default Resolution
        string preferredPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Native Windows: Use C:\ProgramData\StacksAtlas
            preferredPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StacksAtlas");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Native Mac: Use system-wide Application Support
            preferredPath = "/Library/Application Support/StacksAtlas";
        }
        else 
        {
            // Linux/Unix Fallback
            var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            preferredPath = !string.IsNullOrEmpty(commonData) && commonData != "/" 
                ? Path.Combine(commonData, "StacksAtlas") 
                : "/var/lib/stacksatlas";
        }

        // 4. Accessibility Check
        try
        {
            var resolved = EnsureWritableDirectory(preferredPath);

            if (isDev)
            {
                var criticalFiles = new[] { "federation_settings.json", "systemsettings.json", "networksettings.json" };
                foreach (var file in criticalFiles)
                {
                    var fullPath = Path.Combine(resolved, file);
                    if (File.Exists(fullPath))
                    {
                        using var fs = File.Open(fullPath, FileMode.Open, FileAccess.ReadWrite);
                    }
                }
            }

            return resolved;
        }
        catch (Exception)
        {
            if (isDev)
            {
                var localFallback = Path.Combine(AppContext.BaseDirectory, "data");
                return EnsureWritableDirectory(localFallback);
            }

            return preferredPath;
        }
    }

    private static string EnsureWritableDirectory(string preferredPath)
    {
        if (!Directory.Exists(preferredPath)) Directory.CreateDirectory(preferredPath);

        var testPath = Path.Combine(preferredPath, ".permissions_check");
        File.WriteAllText(testPath, DateTime.UtcNow.ToString());
        File.Delete(testPath);
        return preferredPath;
    }

    // --- Directory Accessors ---
    public static string GetLogDirectory() => Path.Combine(BaseDataDir, "logs");
    public static string GetConfigDirectory() => BaseDataDir;
    public static string GetCertDirectory() => Path.Combine(BaseDataDir, "certs");
    public static string GetDatabaseKeyPath() => Path.Combine(BaseDataDir, "db.key");
    public static string GetLicenseKeyPath() => Path.Combine(BaseDataDir, "license.key");
    public static string GetNetworkSettingsPath() => Path.Combine(BaseDataDir, "networksettings.json");
    public static string GetSystemSettingsPath() => Path.Combine(BaseDataDir, "systemsettings.json");

    /// <summary>
    /// Ensures the required directory structure exists on the filesystem.
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        try 
        {
            Directory.CreateDirectory(BaseDataDir);
            Directory.CreateDirectory(GetLogDirectory());
            Directory.CreateDirectory(GetCertDirectory());
        }
        catch (Exception ex)
        {
            // Fallback to console if logging hasn't initialized yet
            Console.WriteLine($"[CRITICAL] Failed to initialize platform directories: {ex.Message}");
            throw;
        }
    }
}
