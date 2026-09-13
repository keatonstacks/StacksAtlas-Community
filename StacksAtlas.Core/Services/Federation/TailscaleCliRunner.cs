using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Federation;

public sealed class TailscaleCliRunner(ILogger<TailscaleCliRunner> logger) : ITailscaleCliRunner
{
    private readonly ILogger<TailscaleCliRunner> _logger = logger;
    private bool? _cliAvailable;

    public bool IsCliAvailable()
    {
        if (_cliAvailable.HasValue)
            return _cliAvailable.Value;

        _cliAvailable = DetectCliAvailable();
        return _cliAvailable.Value;
    }

    public async Task<(bool Success, string Output, string? Error)> RunAsync(string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ResolveCliPath(),
                Arguments = BuildArguments(arguments),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var success = process.ExitCode == 0;

            if (!success)
                _logger.LogDebug("Tailscale CLI exited with code {ExitCode}: {Error}", process.ExitCode, stderr);

            return (success, stdout.Trim(), string.IsNullOrWhiteSpace(stderr) ? null : stderr.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute Tailscale CLI with arguments: {Arguments}", arguments);
            return (false, string.Empty, ex.Message);
        }
    }

    private bool DetectCliAvailable()
    {
        var cliPath = ResolveCliPath();
        var socketPath = ResolveSocketPath();

        if (!string.IsNullOrWhiteSpace(socketPath) && File.Exists(socketPath))
        {
            if (File.Exists(cliPath) || cliPath.Equals("tailscale", StringComparison.Ordinal) || cliPath.Equals("tailscale.exe", StringComparison.OrdinalIgnoreCase))
                return ProbeCliResponds(TimeSpan.FromSeconds(3));
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STACKSATLAS_TAILSCALE_CLI")) && File.Exists(cliPath))
            return ProbeCliResponds(TimeSpan.FromSeconds(3));

        if (OperatingSystem.IsLinux() && File.Exists(cliPath))
            return ProbeCliResponds(TimeSpan.FromSeconds(3));

        if (OperatingSystem.IsWindows())
            return ProbeCliResponds(TimeSpan.FromSeconds(3));

        return false;
    }

    private bool ProbeCliResponds(TimeSpan timeout)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ResolveCliPath(),
                Arguments = BuildArguments("version"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!process.Start())
                return false;

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                _logger.LogDebug("Tailscale CLI version probe timed out after {Seconds}s", timeout.TotalSeconds);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tailscale CLI availability probe failed.");
            return false;
        }
    }

    private static string BuildArguments(string arguments)
    {
        var socketPath = ResolveSocketPath();
        if (string.IsNullOrWhiteSpace(socketPath) || !File.Exists(socketPath))
            return arguments;

        return $"--socket=\"{socketPath}\" {arguments}";
    }

    private static string? ResolveSocketPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("STACKSATLAS_TAILSCALE_SOCKET");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        if (OperatingSystem.IsLinux() && File.Exists("/var/run/tailscale/tailscaled.sock"))
            return "/var/run/tailscale/tailscaled.sock";

        return null;
    }

    private static string ResolveCliPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("STACKSATLAS_TAILSCALE_CLI");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidate = Path.Combine(programFiles, "Tailscale", "tailscale.exe");
            if (File.Exists(candidate))
                return candidate;
            return "tailscale.exe";
        }

        foreach (var candidate in new[] { "/usr/bin/tailscale", "/bin/tailscale", "/usr/local/bin/tailscale" })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return "tailscale";
    }
}
