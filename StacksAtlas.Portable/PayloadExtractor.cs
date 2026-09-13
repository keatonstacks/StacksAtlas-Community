using System.IO.Compression;
using System.Reflection;

namespace StacksAtlas.Portable;

internal static class PayloadExtractor
{
    private const string ResourceName = "StacksAtlas.Portable.portable.payload.zip";

    public static void EnsureExtracted(string expectedVersion)
    {
        Directory.CreateDirectory(PortablePaths.AppDir);
        Directory.CreateDirectory(PortablePaths.DataDir);

        if (File.Exists(PortablePaths.ApiExe) && File.Exists(PortablePaths.VersionFile))
        {
            var current = File.ReadAllText(PortablePaths.VersionFile).Trim();
            var versionMatches = string.Equals(current, expectedVersion, StringComparison.OrdinalIgnoreCase);
            var launcherNewer = IsLauncherNewerThanExtractedPayload();

            if (versionMatches && !launcherNewer)
            {
                PortableLog.Write($"Payload already at version {current}.");
                return;
            }

            if (versionMatches && launcherNewer)
                PortableLog.Write($"Launcher rebuilt at version {current}  -  refreshing embedded payload.");
            else
                PortableLog.Write($"Upgrading payload {current} -> {expectedVersion}.");
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found. Rebuild with portable.payload.zip.");

        var tempZip = Path.Combine(Path.GetTempPath(), $"stacksatlas-portable-{Guid.NewGuid():N}.zip");
        try
        {
            using (var file = File.Create(tempZip))
                stream.CopyTo(file);

            if (Directory.Exists(PortablePaths.AppDir))
            {
                foreach (var file in Directory.EnumerateFiles(PortablePaths.AppDir, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { /* upgrade */ }
                }
            }

            ZipFile.ExtractToDirectory(tempZip, PortablePaths.AppDir, overwriteFiles: true);
            File.WriteAllText(PortablePaths.VersionFile, expectedVersion);
            PortableLog.Write($"Extracted payload version {expectedVersion} to {PortablePaths.AppDir}.");
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    public static string ReadLauncherVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }

    private static bool IsLauncherNewerThanExtractedPayload()
    {
        var launcher = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(launcher) || !File.Exists(launcher) || !File.Exists(PortablePaths.ApiExe))
            return false;

        var launcherTime = File.GetLastWriteTimeUtc(launcher);
        var apiTime = File.GetLastWriteTimeUtc(PortablePaths.ApiExe);
        return launcherTime > apiTime.AddSeconds(2);
    }
}
