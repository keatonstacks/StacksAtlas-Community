using System.Diagnostics;

namespace StacksAtlas.Portable;

internal sealed class ApiProcess : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public static void StopOrphanedApiProcesses()
    {
        PortableWipe.KillApiProcesses();
    }

    public void Start()
    {
        if (!File.Exists(PortablePaths.ApiExe))
            throw new FileNotFoundException("StacksAtlas.API.exe was not found after extraction.", PortablePaths.ApiExe);

        ProcessCleanup.StopStacksAtlasRuntime();

        var startInfo = new ProcessStartInfo
        {
            FileName = PortablePaths.ApiExe,
            WorkingDirectory = PortablePaths.AppDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (!string.IsNullOrEmpty(key))
                startInfo.Environment[key] = entry.Value?.ToString() ?? string.Empty;
        }

        startInfo.Environment["STACKSATLAS_PORTABLE"] = "1";
        startInfo.Environment[PortableWipe.LauncherPathVariable] =
            Environment.ProcessPath ?? Application.ExecutablePath;
        startInfo.Environment["STACKSATLAS_DATADIR"] = PortablePaths.DataDir;
        startInfo.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = PortablePaths.BundleDir;
        startInfo.Environment["ASPNETCORE_URLS"] = "https://127.0.0.1:5001;http://127.0.0.1:5000";

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start StacksAtlas.API.exe.");

        PortableLog.Write($"Started API pid={_process.Id}");
    }

    public void Dispose()
    {
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
