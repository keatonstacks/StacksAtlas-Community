using StacksAtlas.Core.Services.Governance;

namespace StacksAtlas.Tests;

public class FactorySettingsResetTests : IDisposable
{
    private readonly string _tempDir;

    public FactorySettingsResetTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StacksAtlasFactoryReset_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", _tempDir);
        typeof(StacksAtlas.Core.Helpers.PlatformPaths)
            .GetField("_baseDataDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", null);
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    [Fact]
    public void TryApply_DeletesSettingsFilesAndClearsFlag()
    {
        var systemPath = StacksAtlas.Core.Helpers.PlatformPaths.GetSystemSettingsPath();
        var federationPath = Path.Combine(_tempDir, "federation_settings.json");
        File.WriteAllText(systemPath, "{}");
        File.WriteAllText(federationPath, "{}");

        FactorySettingsReset.Stage();
        var applied = FactorySettingsReset.TryApply();

        Assert.True(applied);
        Assert.False(File.Exists(systemPath));
        Assert.False(File.Exists(federationPath));
        Assert.False(File.Exists(FactorySettingsReset.GetFlagPath()));
    }
}
