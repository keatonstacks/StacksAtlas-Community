namespace StacksAtlas.Portable;



internal static class Program

{

    [STAThread]

    private static async Task Main(string[] args)

    {

        PortableLog.Write($"Process starting pid={Environment.ProcessId} args=[{string.Join(' ', args)}]");



        if (args.Contains("--trust-cert", StringComparer.OrdinalIgnoreCase))
        {
            await RunTrustCertAsync();
            return;
        }

        if (args.Contains("--wipe", StringComparer.OrdinalIgnoreCase))

        {

            await RunWipeAsync();

            return;

        }



        try

        {

            ApplicationConfiguration.Initialize();

        }

        catch (Exception ex)

        {

            PortableLog.Write($"WinForms init failed: {ex}");

            MessageBox.Show(

                $"StacksAtlas Portable failed to initialize.\n\n{ex.Message}\n\nSee log:\n{PortablePaths.LaunchLog}",

                "StacksAtlas Portable",

                MessageBoxButtons.OK,

                MessageBoxIcon.Error);

            Environment.Exit(1);

            return;

        }



        if (args.Contains("--tray", StringComparer.OrdinalIgnoreCase))

        {

            using var trayMutex = new Mutex(true, @"Global\StacksAtlas.Portable.Tray", out var ownsTray);

            if (!ownsTray)

            {

                PortableLog.Write("Tray already running  -  exiting duplicate tray instance.");

                return;

            }



            Application.Run(new PortableTrayContext());

            return;

        }



        try

        {

            var orchestrator = new PortableOrchestrator();

            using var cts = new CancellationTokenSource();

            await orchestrator.RunAsync(cts.Token);

        }

        catch (Exception ex)

        {

            PortableLog.Write($"Fatal: {ex}");

            ApiProcess.StopOrphanedApiProcesses();

            MessageBox.Show(

                $"StacksAtlas Portable failed to start.\n\n{ex.Message}\n\nSee log:\n{PortablePaths.LaunchLog}",

                "StacksAtlas Portable",

                MessageBoxButtons.OK,

                MessageBoxIcon.Error);

            Environment.Exit(1);

        }

    }



    private static async Task RunTrustCertAsync()
    {
        PortableLog.Write("TLS trust helper started.");
        var caPath = Path.Combine(PortablePaths.DataDir, "certs", StacksAtlas.Core.Services.Security.ApplianceTlsTrust.CaFileName);
        var ok = await StacksAtlas.Core.Services.Security.ApplianceTlsTrust.WaitAndTrustApplianceCertificateAsync(
            caPath,
            TimeSpan.FromSeconds(20),
            PortableLog.Write);
        PortableLog.Write(ok ? "TLS trust helper completed." : "TLS trust helper failed.");
        Environment.Exit(ok ? 0 : 1);
    }

    private static async Task RunWipeAsync()

    {

        PortableLog.Write("Wipe helper started.");
        PortableWipe.KillApiProcesses();
        PortableWipe.KillOtherPortableProcesses(exceptProcessId: Environment.ProcessId);
        await Task.Delay(2000);



        var removed = PortableWipe.TryExecutePendingRemoval();

        if (!removed && Directory.Exists(PortablePaths.RootDir))

            removed = PortableWipe.TryRemoveInstallationRoot(PortablePaths.RootDir);



        PortableLog.Write(removed ? "Wipe completed." : "Wipe failed  -  data may remain until next launch.");

        Environment.Exit(removed ? 0 : 1);

    }

}


