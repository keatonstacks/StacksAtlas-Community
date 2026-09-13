using System.Net.Http;

namespace StacksAtlas.Portable;

internal sealed class PortableTrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly HttpClient _http;
    private readonly System.Windows.Forms.Timer _closeWatch;
    private bool _wipeInProgress;

    public PortableTrayContext()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        _tray = new NotifyIcon
        {
            Icon = PortableIcons.LoadTrayIcon(),
            Visible = true,
            Text = "StacksAtlas Portable"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open StacksAtlas", null, (_, _) => PortableOrchestrator.OpenBrowser());
        menu.Items.Add("Close & remove", null, async (_, _) => await CloseAndRemoveAsync());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => PortableOrchestrator.OpenBrowser();

        _closeWatch = new System.Windows.Forms.Timer { Interval = 400 };
        _closeWatch.Tick += (_, _) =>
        {
            if (_wipeInProgress || !File.Exists(PortableWipe.PendingWipeMarkerPath))
                return;

            PortableLog.Write("Close marker detected  -  supervisor wiping data.");
            PerformWipeAndExit();
        };
        _closeWatch.Start();
    }

    private async Task CloseAndRemoveAsync()
    {
        if (_wipeInProgress)
            return;

        _tray.Visible = false;
        _closeWatch.Stop();

        try
        {
            File.WriteAllText(PortableWipe.PendingWipeMarkerPath, PortablePaths.RootDir);
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await client.PostAsync(PortablePaths.CloseUrl, null);
            PortableLog.Write($"Close API responded {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            PortableLog.Write($"Close API failed: {ex.Message}  -  wiping locally.");
        }

        await Task.Delay(1200);
        PerformWipeAndExit();
    }

    private void PerformWipeAndExit()
    {
        if (_wipeInProgress)
            return;

        _wipeInProgress = true;
        _closeWatch.Stop();
        _tray.Visible = false;

        PortableLog.Write("Supervisor wipe starting.");
        PortableWipe.KillApiProcesses();
        Thread.Sleep(800);

        var removed = PortableWipe.TryExecutePendingRemoval()
            || PortableWipe.TryRemoveInstallationRoot(PortablePaths.RootDir);

        PortableLog.Write(removed ? "Supervisor wipe completed." : "Supervisor wipe failed.");
        Environment.Exit(removed ? 0 : 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closeWatch.Stop();
            _closeWatch.Dispose();
            _tray.Dispose();
            _http.Dispose();
        }

        base.Dispose(disposing);
    }
}
