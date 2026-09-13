using System.Diagnostics;

namespace StacksAtlas.Portable;

internal static class PortableSessionGuard
{
    public static void PrepareFreshSession()
    {
        TerminateStaleWipeScripts();
        PortableWipe.KillOtherPortableProcesses(exceptProcessId: Environment.ProcessId);

        if (File.Exists(PortableWipe.PendingWipeMarkerPath))
        {
            PortableLog.Write("Completing pending portable data wipe from Close & remove.");
            var removed = PortableWipe.TryExecutePendingRemoval();
            PortableLog.Write(removed
                ? "Pending portable data wipe completed."
                : "Pending portable data wipe still in progress  -  will retry.");
        }

        Directory.CreateDirectory(PortablePaths.RootDir);
        Directory.CreateDirectory(PortablePaths.AppDir);
        Directory.CreateDirectory(PortablePaths.DataDir);
        Directory.CreateDirectory(PortablePaths.BundleDir);
    }

    private static void TerminateStaleWipeScripts()
    {
        if (!OperatingSystem.IsWindows())
            return;

        foreach (var script in new[] { "StacksAtlas.portable-wipe.ps1", "StacksAtlas.portable-wipe-fallback.ps1" })
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /FI \"IMAGENAME eq powershell.exe\" /FI \"COMMANDLINE eq *{script}*\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                })?.WaitForExit(2000);
            }
            catch
            {
                // Best effort  -  clears locks from older portable builds.
            }
        }
    }
}
