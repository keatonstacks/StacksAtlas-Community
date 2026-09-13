using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Updates;

public sealed class WindowsUpdateApplyRunner(ILogger<WindowsUpdateApplyRunner> logger)
{
    public void ScheduleMsiInstall(string msiPath, string installLogPath)
    {
        var scriptPath = WritePowerShellScript(
            "apply-msi.ps1",
            $$"""
            $ErrorActionPreference = 'Stop'
            Stop-Service -Name StacksAtlas -Force -ErrorAction SilentlyContinue
            Get-Process -Name 'StacksAtlas','StacksAtlas.API' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3
            $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList @(
              '/i', '{{EscapePs(msiPath)}}', '/qn', '/norestart', '/l*v', '{{EscapePs(installLogPath)}}'
            ) -Wait -PassThru
            if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { exit $p.ExitCode }
            Start-Service -Name StacksAtlas -ErrorAction SilentlyContinue
            exit 0
            """);

        LaunchDetachedPowerShell(scriptPath);
        logger.LogInformation("Scheduled MSI update install via {Script}", scriptPath);
    }

    public void SchedulePortableReplace(string launcherPath, string stagedPortableExe)
    {
        if (!File.Exists(launcherPath))
            throw new FileNotFoundException("Portable launcher not found.", launcherPath);

        var scriptPath = WritePowerShellScript(
            "apply-portable.ps1",
            $$"""
            $ErrorActionPreference = 'Stop'
            $launcher = '{{EscapePs(launcherPath)}}'
            $staged = '{{EscapePs(stagedPortableExe)}}'
            Get-Process -Name 'StacksAtlas-Portable','StacksAtlas','StacksAtlas.API' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3
            $backup = "$launcher.pre-update.bak"
            if (Test-Path $launcher) { Copy-Item -LiteralPath $launcher -Destination $backup -Force }
            Copy-Item -LiteralPath $staged -Destination $launcher -Force
            Start-Process -FilePath $launcher -WorkingDirectory (Split-Path -Parent $launcher)
            exit 0
            """);

        LaunchDetachedPowerShell(scriptPath);
        logger.LogInformation("Scheduled portable launcher replace via {Script}", scriptPath);
    }

    private static string WritePowerShellScript(string fileName, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "StacksAtlas-updates");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static void LaunchDetachedPowerShell(string scriptPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static string EscapePs(string value) => value.Replace("'", "''");
}
