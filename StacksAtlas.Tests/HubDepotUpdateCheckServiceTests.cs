using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Services.Updates;

namespace StacksAtlas.Tests;

public sealed class HubDepotUpdateCheckServiceTests
{
    private sealed class StubDepot : IUpdateDepotService
    {
        public required ReleaseManifest Manifest { get; init; }
        public required string Version { get; init; }
        public string? ArtifactPath { get; init; }

        public UpdateDepotStatus GetStatus() => new()
        {
            DepotRoot = "x",
            Channels = [],
            QueriedUtc = DateTime.UtcNow
        };

        public Task<UpdateDepotStageResult> StageAsync(string? channel = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool TryGetLatestStagedRelease(string? channel, out string version, out ReleaseManifest manifest, out string versionDirectory)
        {
            version = Version;
            manifest = Manifest;
            versionDirectory = "x";
            return true;
        }

        public bool TryResolveArtifactPath(string? channel, string version, string artifactKey, out string path)
        {
            path = ArtifactPath ?? "";
            return !string.IsNullOrEmpty(ArtifactPath);
        }
    }

    [Fact]
    public void CheckForNode_marks_update_available_and_rewrites_download_url()
    {
        var depot = new StubDepot
        {
            Version = "1.9.2",
            Manifest = new ReleaseManifest
            {
                ManifestVersion = 1,
                Channel = "stable",
                PublishedUtc = DateTime.UtcNow,
                Artifacts = new Dictionary<string, ReleaseArtifactEntry>(StringComparer.Ordinal)
                {
                    [UpdateArtifactKeys.WinX64Msi] = new()
                    {
                        Version = "1.9.2",
                        Url = "https://releases.stacksatlas.com/stable/StacksAtlas.msi",
                        Sha256 = "abc"
                    }
                }
            }
        };

        var service = new HubDepotUpdateCheckService(
            depot,
            NullLogger<HubDepotUpdateCheckService>.Instance);

        var result = service.CheckForNode(
            "stable",
            currentVersion: "1.9.1",
            artifactKey: UpdateArtifactKeys.WinX64Msi,
            publicArtifactBaseUrl: "https://hub.local:5002/api/federation/updates/artifacts");

        Assert.Equal(UpdateCheckStatuses.UpdateAvailable, result.Status);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.9.2", result.AvailableVersion);
        Assert.Equal("1.9.1", result.CurrentVersion);
        Assert.Equal(
            "https://hub.local:5002/api/federation/updates/artifacts/win-x64-msi/1.9.2?channel=stable",
            result.DownloadUrl);
        Assert.Equal("abc", result.Sha256);
        Assert.True(result.ApplySupported);
    }

    [Fact]
    public void CheckForNode_up_to_date_when_node_matches_staged()
    {
        var depot = new StubDepot
        {
            Version = "1.9.2",
            Manifest = new ReleaseManifest
            {
                ManifestVersion = 1,
                Channel = "preview",
                PublishedUtc = DateTime.UtcNow,
                Artifacts = new Dictionary<string, ReleaseArtifactEntry>(StringComparer.Ordinal)
                {
                    [UpdateArtifactKeys.WinX64Msi] = new()
                    {
                        Version = "1.9.2",
                        Url = "https://releases.stacksatlas.com/preview/StacksAtlas.msi",
                        Sha256 = "def"
                    }
                }
            }
        };

        var service = new HubDepotUpdateCheckService(
            depot,
            NullLogger<HubDepotUpdateCheckService>.Instance);

        var result = service.CheckForNode(
            "preview",
            currentVersion: "1.9.2",
            artifactKey: UpdateArtifactKeys.WinX64Msi,
            publicArtifactBaseUrl: "https://hub.local:5002/api/federation/updates/artifacts");

        Assert.Equal(UpdateCheckStatuses.UpToDate, result.Status);
        Assert.False(result.UpdateAvailable);
    }
}
