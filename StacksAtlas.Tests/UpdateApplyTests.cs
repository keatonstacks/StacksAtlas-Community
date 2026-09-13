using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Services.Updates;

namespace StacksAtlas.Tests;

public sealed class UpdateApplyTests
{
    [Theory]
    [InlineData("https://releases.stacksatlas.com/preview/StacksAtlas.msi", true)]
    [InlineData("https://releases.stacksatlas.com/stable/StacksAtlas-Portable.exe", true)]
    [InlineData("https://192.168.1.10:5002/api/federation/updates/artifacts/win-x64-msi/1.9.2", true)]
    [InlineData("http://releases.stacksatlas.com/preview/StacksAtlas.msi", false)]
    [InlineData("https://evil.example.com/StacksAtlas.msi", false)]
    [InlineData("not-a-url", false)]
    public void TryValidateDownloadUrl_enforces_https_release_host(string url, bool expected)
    {
        var ok = UpdateArtifactDownloader.TryValidateDownloadUrl(url, out var uri);
        Assert.Equal(expected, ok);
        if (expected && url.Contains("releases.stacksatlas.com", StringComparison.Ordinal))
        {
            Assert.Equal("releases.stacksatlas.com", uri.Host);
        }
    }

    [Fact]
    public void IsHubFederationArtifactUrl_detects_depot_paths()
    {
        Assert.True(UpdateArtifactDownloader.IsHubFederationArtifactUrl(
            new Uri("https://hub.local:5002/api/federation/updates/artifacts/win-x64-msi/1.9.2")));
        Assert.False(UpdateArtifactDownloader.IsHubFederationArtifactUrl(
            new Uri("https://releases.stacksatlas.com/stable/StacksAtlas.msi")));
    }

    [Fact]
    public void Resolve_apply_hints_for_windows_artifacts()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var (msiSupported, msiMode) = UpdateApplyCapabilities.Resolve(
            UpdateArtifactKeys.WinX64Msi,
            "https://releases.stacksatlas.com/preview/StacksAtlas.msi");
        Assert.True(msiSupported);
        Assert.Equal(UpdateApplyCapabilities.ApplyModeInApp, msiMode);

        var (portableSupported, portableMode) = UpdateApplyCapabilities.Resolve(
            UpdateArtifactKeys.WinX64Portable,
            "https://releases.stacksatlas.com/preview/StacksAtlas-Portable.exe");
        Assert.True(portableSupported);
        Assert.Equal(UpdateApplyCapabilities.ApplyModeInApp, portableMode);

        var (dockerSupported, dockerMode) = UpdateApplyCapabilities.Resolve(
            UpdateArtifactKeys.LinuxDocker,
            "https://releases.stacksatlas.com/preview/manifest.json");
        Assert.False(dockerSupported);
        Assert.Equal(UpdateApplyCapabilities.ApplyModeGuided, dockerMode);
    }

    [Fact]
    public void ShouldCreatePreUpdateSnapshot_is_false_for_portable_artifact()
    {
        Assert.False(UpdateApplySnapshotPolicy.ShouldCreatePreUpdateSnapshot(UpdateArtifactKeys.WinX64Portable));
        Assert.True(UpdateApplySnapshotPolicy.ShouldCreatePreUpdateSnapshot(UpdateArtifactKeys.WinX64Msi));
    }

    [Fact]
    public void Resolve_without_download_url_is_manual()
    {
        var (supported, mode) = UpdateApplyCapabilities.Resolve(UpdateArtifactKeys.WinX64Msi, null);
        Assert.False(supported);
        Assert.Equal(UpdateApplyCapabilities.ApplyModeManual, mode);
    }

    [Fact]
    public void Resolve_linux_docker_is_guided_without_download_url()
    {
        var (supported, mode) = UpdateApplyCapabilities.Resolve(UpdateArtifactKeys.LinuxDocker, null);
        Assert.False(supported);
        Assert.Equal(UpdateApplyCapabilities.ApplyModeGuided, mode);
    }
}
