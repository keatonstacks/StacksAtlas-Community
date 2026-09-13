using System.Diagnostics;

namespace StacksAtlas.Portable;

internal static class ProcessCleanup
{
    public static void StopStacksAtlasRuntime()
    {
        try
        {
            using var service = new System.ServiceProcess.ServiceController("StacksAtlas");
            if (service.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                service.Stop();
        }
        catch
        {
            // Service not installed  -  expected for portable.
        }

        foreach (var process in Process.GetProcessesByName("StacksAtlas.API"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
