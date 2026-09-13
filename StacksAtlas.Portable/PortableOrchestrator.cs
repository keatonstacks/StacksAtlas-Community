using System.Diagnostics;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.Portable;

internal sealed class PortableOrchestrator
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        PortableLog.Write("Portable orchestrator starting.");
        PortableSessionGuard.PrepareFreshSession();

        var version = PayloadExtractor.ReadLauncherVersion();
        PayloadExtractor.EnsureExtracted(version);

        using var api = new ApiProcess();
        api.Start();

        var ready = await HealthProbe.WaitForApiAsync(TimeSpan.FromSeconds(90), cancellationToken);
        if (!ready)
            throw new TimeoutException("StacksAtlas API did not become healthy within 90 seconds.");

        // Trust is opt-in from onboarding  -  no Windows Security Warning on launch.
        OpenBrowser(firstLaunch: true);
        PortableLog.Write("Portable session ready  -  running tray in supervisor process.");

        using var trayMutex = new Mutex(true, @"Global\StacksAtlas.Portable.Tray", out var ownsTray);
        if (!ownsTray)
        {
            PortableLog.Write("Another tray instance is active  -  supervisor exiting.");
            return;
        }

        Application.Run(new PortableTrayContext());
    }

    public static void OpenBrowser(bool firstLaunch = false)
    {
        var url = firstLaunch
            ? PortablePaths.DashboardUrl
            : ApplianceDashboardUrls.Resolve(
                ApplianceDashboardUrls.LoadSettingsOrDefault(PortablePaths.DataDir),
                PortablePaths.DataDir);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            PortableLog.Write($"Opened browser: {url}");
        }
        catch (Exception ex)
        {
            PortableLog.Write($"Failed to open browser: {ex.Message}");
        }
    }
}

