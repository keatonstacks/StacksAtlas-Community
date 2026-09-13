using System.Diagnostics;

using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;



namespace StacksAtlas.Core.Helpers;



/// <summary>

/// Removes portable data after the API process exits (files are locked while running).

/// </summary>

public static class PortableDataCleanup

{

    /// <summary>Legacy marker name inside the data directory (pre-v1.5.0 portable).</summary>

    public const string PendingWipeFileName = ".pending-wipe";



    /// <summary>

    /// Temp-file marker outside the tree being deleted. Contains the absolute path to remove.

    /// Survives partial wipes and is cleared when removal succeeds.

    /// </summary>

    public const string PendingWipeMarkerFileName = "StacksAtlas.portable.pending-wipe";



    public const string WipeLogFileName = "StacksAtlas.portable-wipe.log";

    /// <summary>Override wipe marker path (tests only). Must match StacksAtlas.Portable PortableWipe.</summary>
    public const string WipeMarkerEnvironmentVariable = "STACKSATLAS_PORTABLE_WIPE_MARKER";

    private static bool SkipGlobalProcessKill { get; set; }

    public static string GetPendingWipeMarkerPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(WipeMarkerEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return Path.Combine(Path.GetTempPath(), PendingWipeMarkerFileName);
    }

    /// <summary>Isolates wipe marker and optionally skips killing live portable/API processes (unit tests).</summary>
    public static void ConfigureWipeTesting(string? markerFilePath, bool skipGlobalProcessKill)
    {
        if (string.IsNullOrWhiteSpace(markerFilePath))
            Environment.SetEnvironmentVariable(WipeMarkerEnvironmentVariable, null);
        else
            Environment.SetEnvironmentVariable(WipeMarkerEnvironmentVariable, markerFilePath);

        SkipGlobalProcessKill = skipGlobalProcessKill;
    }

    public static void ClearPendingWipeMarker() => TryDeleteFile(GetPendingWipeMarkerPath());

    public static string GetWipeLogPath() =>
        Path.Combine(Path.GetTempPath(), WipeLogFileName);

    /// <summary>

    /// Directory to delete once the process has stopped.

    /// Mac: ~/StacksAtlas-Trial. Windows: %LOCALAPPDATA%\StacksAtlas (app + data).

    /// </summary>

    public static string ResolveCleanupDirectory()

    {

        var dataDir = PlatformPaths.BaseDataDir;



        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))

        {

            var parent = Directory.GetParent(dataDir)?.FullName;

            if (parent != null &&

                string.Equals(Path.GetFileName(parent), "StacksAtlas", StringComparison.OrdinalIgnoreCase))

                return parent;

        }



        return dataDir;

    }



    /// <summary>
    /// Writes a wipe marker and shuts down the API. The portable supervisor process
    /// detects the marker and wipes <c>app</c> + <c>data</c> before exiting.
    /// </summary>
    public static void ScheduleRemovalAndExit(ILogger? logger = null)
    {
        if (!PortableMode.IsEnabled)
            throw new InvalidOperationException("Portable data cleanup is only available in portable mode.");

        var targetDir = ResolveCleanupDirectory();
        var wipeMarker = GetPendingWipeMarkerPath();
        logger?.LogInformation("Portable close signalled  -  supervisor will wipe {Directory}", targetDir);

        try
        {
            File.WriteAllText(wipeMarker, targetDir);
            AppendWipeLog($"Close signalled  -  supervisor will wipe {targetDir}");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not write portable wipe marker at {Path}", wipeMarker);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(800);
            Environment.Exit(0);
        });
    }



    /// <summary>Kills API processes so extracted files under <c>app</c> can be deleted.</summary>

    public static void KillApiProcesses(ILogger? logger = null)

    {

        var currentPid = Environment.ProcessId;



        foreach (var process in Process.GetProcessesByName("StacksAtlas.API"))

        {

            try

            {

                if (process.HasExited || process.Id == currentPid)

                    continue;



                process.Kill(entireProcessTree: true);

                process.WaitForExit(5000);

            }

            catch (Exception ex)

            {

                logger?.LogDebug(ex, "Could not stop API process pid={Pid}", process.Id);

            }

            finally

            {

                process.Dispose();

            }

        }

    }



    /// <summary>Kills portable launcher processes (tray/supervisor). Used by tests and relaunch hygiene.</summary>

    public static void KillTrayProcesses(ILogger? logger = null, int? exceptProcessId = null)

    {

        foreach (var process in Process.GetProcessesByName("StacksAtlas-Portable"))

        {

            try

            {

                if (process.HasExited)

                    continue;



                if (exceptProcessId.HasValue && process.Id == exceptProcessId.Value)

                    continue;



                process.Kill(entireProcessTree: true);

                process.WaitForExit(3000);

            }

            catch (Exception ex)

            {

                logger?.LogDebug(ex, "Could not stop portable launcher pid={Pid}", process.Id);

            }

            finally

            {

                process.Dispose();

            }

        }

    }



    /// <summary>Spawns StacksAtlas-Portable.exe --wipe so Close &amp; remove works from the browser or tray.</summary>

    public static bool SpawnDetachedWipeHelper(ILogger? logger = null)

    {

        var launcher = Environment.GetEnvironmentVariable(PortableMode.LauncherPathVariable);

        if (string.IsNullOrWhiteSpace(launcher) || !File.Exists(launcher))

        {

            logger?.LogWarning(

                "Portable wipe helper not spawned  -  {Variable} is missing or invalid.",

                PortableMode.LauncherPathVariable);

            AppendWipeLog("Wipe helper not spawned  -  launcher path unavailable.");

            return false;

        }



        try

        {

            Process.Start(new ProcessStartInfo

            {

                FileName = launcher,

                Arguments = "--wipe",

                UseShellExecute = false,

                CreateNoWindow = true,

                WindowStyle = ProcessWindowStyle.Hidden

            });

            AppendWipeLog($"Spawned wipe helper: {launcher}");

            logger?.LogInformation("Spawned portable wipe helper at {Launcher}", launcher);

            return true;

        }

        catch (Exception ex)

        {

            logger?.LogWarning(ex, "Could not spawn portable wipe helper.");

            AppendWipeLog($"Failed to spawn wipe helper: {ex.Message}");

            return false;

        }

    }



    /// <summary>

    /// Clears wipe markers without deleting data (tests / explicit abort only).

    /// </summary>

    public static void CancelPendingRemoval(string? legacyDataDirectory = null)

    {

        try

        {

            var marker = GetPendingWipeMarkerPath();

            if (File.Exists(marker))

                File.Delete(marker);

        }

        catch

        {

            // Best effort.

        }



        if (string.IsNullOrWhiteSpace(legacyDataDirectory))

            return;



        try

        {

            var legacyMarker = Path.Combine(legacyDataDirectory, PendingWipeFileName);

            if (File.Exists(legacyMarker))

                File.Delete(legacyMarker);

        }

        catch

        {

            // Best effort.

        }

    }



    /// <summary>

    /// Removes a portable installation root (app + data). Used by tests and relaunch recovery.

    /// </summary>

    public static bool TryRemovePortableInstallation(string rootDirectory, ILogger? logger = null, int maxAttempts = 12)

    {

        if (string.IsNullOrWhiteSpace(rootDirectory))

            return false;



        var normalized = Path.GetFullPath(rootDirectory.Trim());

        if (!Directory.Exists(normalized))

            return true;



        if (!SkipGlobalProcessKill)
        {
            AppendWipeLog($"Removing portable installation at {normalized}");
            logger?.LogInformation("Removing portable installation at {Directory}", normalized);
        }
        else
        {
            logger?.LogDebug("Removing portable installation at {Directory} (test isolation - skip global process kill)", normalized);
        }

        var removed = TryRemoveDirectory(normalized, logger, maxAttempts);

        if (removed && !Directory.Exists(normalized))

        {

            AppendWipeLog($"Removed {normalized}");

            logger?.LogInformation("Portable data removed: {Directory}", normalized);

            return true;

        }



        AppendWipeLog($"Portable removal incomplete for {normalized}");

        return false;

    }



    /// <summary>

    /// Executes a pending Close &amp; remove wipe immediately (used on relaunch).

    /// </summary>

    public static bool TryExecutePendingRemoval(ILogger? logger = null, int maxAttempts = 12)

    {

        var marker = GetPendingWipeMarkerPath();

        if (!File.Exists(marker))

            return false;



        string targetDir;

        try

        {

            targetDir = NormalizeMarkerPath(File.ReadAllText(marker));

        }

        catch (Exception ex)

        {

            logger?.LogWarning(ex, "Could not read portable wipe marker at {Path}", marker);

            return false;

        }



        if (string.IsNullOrWhiteSpace(targetDir))

            return false;



        AppendWipeLog($"Starting pending wipe for {targetDir}");

        logger?.LogInformation("Executing pending portable data wipe for {Directory}", targetDir);



        var removed = TryRemovePortableInstallation(targetDir, logger, maxAttempts);

        if (removed)

        {

            TryDeleteFile(marker);

            TryDeleteLegacyMarker(targetDir);

            return true;

        }



        AppendWipeLog($"Pending wipe incomplete for {targetDir}");

        return false;

    }



    private static bool TryRemoveDirectory(string directory, ILogger? logger, int maxAttempts)

    {

        if (!Directory.Exists(directory))

            return true;



        if (!SkipGlobalProcessKill)
        {
            KillApiProcesses(logger);
            KillTrayProcesses(logger);
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++)

        {

            try

            {

                if (!SkipGlobalProcessKill)
                    KillApiProcesses(logger);

                TryRemovePortableSubtrees(directory, logger);

                if (Directory.Exists(directory))

                    Directory.Delete(directory, recursive: true);



                if (!Directory.Exists(directory))

                    return true;



                TryWindowsForceRemove(directory, logger);

                if (!Directory.Exists(directory))

                    return true;

            }

            catch (Exception ex) when (attempt < maxAttempts)

            {

                logger?.LogDebug(ex, "Portable wipe attempt {Attempt}/{MaxAttempts} waiting for locks on {Directory}",

                    attempt, maxAttempts, directory);

            }

            catch (Exception ex)

            {

                logger?.LogWarning(ex, "Portable wipe failed on final attempt for {Directory}", directory);

                AppendWipeLog($"Final wipe attempt failed for {directory}: {ex.Message}");

            }



            if (attempt < maxAttempts)

                Thread.Sleep(500);

        }



        return !Directory.Exists(directory);

    }



    private static void TryRemovePortableSubtrees(string root, ILogger? logger)

    {

        foreach (var sub in new[] { "data", "app" })

        {

            var path = Path.Combine(root, sub);

            if (!Directory.Exists(path))

                continue;



            try

            {

                Directory.Delete(path, recursive: true);

            }

            catch (Exception ex)

            {

                logger?.LogDebug(ex, "Could not delete portable subtree {Path}", path);

                TryWindowsForceRemove(path, logger);

            }

        }

    }



    private static void TryWindowsForceRemove(string directory, ILogger? logger)

    {

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !Directory.Exists(directory))

            return;



        try

        {

            using var process = Process.Start(new ProcessStartInfo

            {

                FileName = "cmd.exe",

                Arguments = $"/c rd /s /q \"{directory}\"",

                CreateNoWindow = true,

                UseShellExecute = false,

                WindowStyle = ProcessWindowStyle.Hidden

            });



            process?.WaitForExit(2000);

        }

        catch (Exception ex)

        {

            logger?.LogDebug(ex, "Windows force-remove fallback failed for {Directory}", directory);

        }

    }



    private static string NormalizeMarkerPath(string raw) =>

        Path.GetFullPath(raw.Trim().Trim('\uFEFF', '\u200B'));



    private static void TryDeleteLegacyMarker(string cleanupRoot)

    {

        try

        {

            var legacyMarker = Path.Combine(cleanupRoot, "data", PendingWipeFileName);

            if (File.Exists(legacyMarker))

                File.Delete(legacyMarker);

        }

        catch

        {

            // Best effort.

        }

    }



    private static void TryDeleteFile(string path)

    {

        try

        {

            if (File.Exists(path))

                File.Delete(path);

        }

        catch

        {

            // Best effort.

        }

    }



    private static void AppendWipeLog(string message)

    {

        try

        {

            File.AppendAllText(GetWipeLogPath(), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");

        }

        catch

        {

            // Best effort.

        }

    }

}


