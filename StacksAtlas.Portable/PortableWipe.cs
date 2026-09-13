using System.Diagnostics;



namespace StacksAtlas.Portable;



/// <summary>

/// Lightweight wipe helpers for the launcher only  -  no StacksAtlas.Core dependency.

/// Marker paths must stay in sync with <see cref="StacksAtlas.Core.Helpers.PortableDataCleanup"/>.

/// </summary>

internal static class PortableWipe

{

    private const int WipeAttempts = 8;
    private const int RetryDelayMs = 750;



    internal const string PendingWipeMarkerFileName = "StacksAtlas.portable.pending-wipe";

    internal const string LauncherPathVariable = "STACKSATLAS_PORTABLE_LAUNCHER";



    internal static string PendingWipeMarkerPath =>
        Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE_WIPE_MARKER") is { Length: > 0 } path
            ? path
            : Path.Combine(Path.GetTempPath(), PendingWipeMarkerFileName);



    internal static void KillOtherPortableProcesses(int? exceptProcessId = null)

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

            catch

            {

                // Best effort.

            }

            finally

            {

                process.Dispose();

            }

        }

    }



    internal static void KillApiProcesses()

    {

        foreach (var process in Process.GetProcessesByName("StacksAtlas.API"))

        {

            try

            {

                if (!process.HasExited)

                {

                    process.Kill(entireProcessTree: true);

                    process.WaitForExit(5000);

                }

            }

            catch

            {

                // Best effort.

            }

            finally

            {

                process.Dispose();

            }

        }

    }



    internal static bool TryRemoveInstallationRoot(string rootDirectory, int maxAttempts = WipeAttempts)

    {

        if (string.IsNullOrWhiteSpace(rootDirectory))

            return false;



        var root = Path.GetFullPath(rootDirectory.Trim());

        if (!Directory.Exists(root))

            return true;



        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            KillApiProcesses();
            KillOtherPortableProcesses(exceptProcessId: Environment.ProcessId);
            TryForceRemove(root);

            if (!Directory.Exists(root))

                return true;



            if (attempt < maxAttempts)

                Thread.Sleep(RetryDelayMs);

        }



        return !Directory.Exists(root);

    }



    internal static bool TryExecutePendingRemoval(int maxAttempts = WipeAttempts)

    {

        var marker = PendingWipeMarkerPath;

        if (!File.Exists(marker))

            return false;



        string targetDir;

        try

        {

            targetDir = Path.GetFullPath(File.ReadAllText(marker).Trim().Trim('\uFEFF', '\u200B'));

        }

        catch

        {

            return false;

        }



        if (string.IsNullOrWhiteSpace(targetDir))

            return false;



        var removed = TryRemoveInstallationRoot(targetDir, maxAttempts);

        if (removed)

            ClearPendingWipeMarker();



        return removed;

    }



    internal static void SpawnWipeHelper()
    {
        var launcher = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(launcher) || !File.Exists(launcher))
            return;

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
        }
        catch
        {
            // Best effort.
        }
    }

    internal static void ClearPendingWipeMarker()

    {

        try

        {

            if (File.Exists(PendingWipeMarkerPath))

                File.Delete(PendingWipeMarkerPath);

        }

        catch

        {

            // Best effort.

        }

    }



    private static void TryForceRemove(string root)

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

            catch

            {

                RunRd(path);

            }

        }



        if (!Directory.Exists(root))

            return;



        try

        {

            Directory.Delete(root, recursive: true);

        }

        catch

        {

            RunRd(root);

        }

    }



    private static void RunRd(string path)

    {

        try

        {

            using var process = Process.Start(new ProcessStartInfo

            {

                FileName = "cmd.exe",

                Arguments = $"/c rd /s /q \"{path}\"",

                CreateNoWindow = true,

                UseShellExecute = false,

                WindowStyle = ProcessWindowStyle.Hidden

            });



            process?.WaitForExit(3000);

        }

        catch

        {

            // Best effort.

        }

    }

}


