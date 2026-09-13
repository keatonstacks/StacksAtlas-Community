using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.State;

namespace StacksAtlas.Tests;

[Collection("PortableEnvironment")]
public class PortableModeTests
{
    [Fact]
    public void AllowLanBind_SetOnlyWhenPortableAndEnvTrue()
    {
        var previousPortable = Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE");
        var previousBindLan = Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE_BIND_LAN");

        try
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", "1");
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE_BIND_LAN", "1");
            PortableMode.InitializeFromEnvironment();
            Assert.True(PortableMode.AllowLanBind);

            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE_BIND_LAN", "0");
            PortableMode.InitializeFromEnvironment();
            Assert.False(PortableMode.AllowLanBind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", previousPortable);
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE_BIND_LAN", previousBindLan);
            PortableMode.InitializeFromEnvironment();
        }
    }

    [Fact]
    public void PlatformPaths_UsesTrialDirectory_WhenPortableEnabled()
    {
        var temp = Path.Combine(Path.GetTempPath(), "stacksatlas-portable-" + Guid.NewGuid().ToString("N"));
        var previousPortable = Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE");
        var previousDataDir = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");

        try
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", "1");
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", temp);
            PortableMode.InitializeFromEnvironment();
            ResetPlatformPathsCache();

            Assert.Equal(temp, PlatformPaths.BaseDataDir);
            Assert.True(ExecutionState.IsPortable);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", previousPortable);
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", previousDataDir);
            PortableMode.InitializeFromEnvironment();
            ResetPlatformPathsCache();
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void HubModeGuard_BlocksHubStartup_InPortableMode()
    {
        var previousPortable = Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE");
        var previousDataDir = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        var temp = Path.Combine(Path.GetTempPath(), "stacksatlas-portable-hub-" + Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", "1");
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", temp);
            PortableMode.InitializeFromEnvironment();
            ResetPlatformPathsCache();

            var mode = HubModeGuard.ResolveStartupMode(StacksAtlas.Core.Models.ExecutionMode.Hub);

            Assert.Equal(StacksAtlas.Core.Models.ExecutionMode.Standalone, mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", previousPortable);
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", previousDataDir);
            PortableMode.InitializeFromEnvironment();
            ResetPlatformPathsCache();
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void HubModeGuard_IgnoresHubDatabase_InPortableMode()
    {
        var previousPortable = Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE");
        var previousDataDir = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        var temp = Path.Combine(Path.GetTempPath(), "stacksatlas-portable-hubdb-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "StacksAtlas.Hub.db"), string.Empty);

            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", "1");
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", temp);
            PortableMode.InitializeFromEnvironment();
            ResetPlatformPathsCache();

            var mode = HubModeGuard.ResolveStartupMode(StacksAtlas.Core.Models.ExecutionMode.Standalone);

            Assert.Equal(StacksAtlas.Core.Models.ExecutionMode.Standalone, mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", previousPortable);
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", previousDataDir);
            PortableMode.InitializeFromEnvironment();
            ResetPlatformPathsCache();
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    private static void ResetPlatformPathsCache()
    {
        var field = typeof(PlatformPaths).GetField("_baseDataDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(null, null);
    }
}
