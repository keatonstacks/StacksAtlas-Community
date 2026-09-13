using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Services.Updates;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Tests;

public sealed class UpdateDepotPathsTests
{
    [Fact]
    public void NormalizeChannel_defaults_and_accepts_preview()
    {
        Assert.Equal("stable", UpdateDepotPaths.NormalizeChannel(null));
        Assert.Equal("stable", UpdateDepotPaths.NormalizeChannel(""));
        Assert.Equal("stable", UpdateDepotPaths.NormalizeChannel("STABLE"));
        Assert.Equal("preview", UpdateDepotPaths.NormalizeChannel("Preview"));
        Assert.Equal("stable", UpdateDepotPaths.NormalizeChannel("nightly"));
    }

    [Fact]
    public void GetVersionDirectory_uses_base_data_dir_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "sa-depot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var versionDir = UpdateDepotPaths.GetVersionDirectory("preview", "1.9.2", root);
            Assert.Equal(
                Path.Combine(root, "updates", "depot", "preview", "1.9.2"),
                versionDir);
            Assert.Equal(Path.Combine(root, "updates", "depot"), UpdateDepotPaths.GetDepotRoot(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveFileName_maps_known_artifact_keys()
    {
        Assert.Equal("StacksAtlas.msi", UpdateDepotArtifactFiles.ResolveFileName(UpdateArtifactKeys.WinX64Msi));
        Assert.Equal("StacksAtlas-Portable.exe", UpdateDepotArtifactFiles.ResolveFileName(UpdateArtifactKeys.WinX64Portable));
        Assert.Equal("StacksAtlas.dmg", UpdateDepotArtifactFiles.ResolveFileName(UpdateArtifactKeys.OsxUniversalDmg));
        Assert.Equal(UpdateDepotArtifactFiles.LinuxDockerMetaFileName, UpdateDepotArtifactFiles.ResolveFileName(UpdateArtifactKeys.LinuxDocker));
        Assert.Null(UpdateDepotArtifactFiles.ResolveFileName("unknown-key"));
    }

    [Fact]
    public void TryFindLatestVersionDirectory_picks_highest_semver()
    {
        var root = Path.Combine(Path.GetTempPath(), "sa-depot-latest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(UpdateDepotPaths.GetVersionDirectory("stable", "1.9.0", root));
            Directory.CreateDirectory(UpdateDepotPaths.GetVersionDirectory("stable", "1.9.2", root));
            Directory.CreateDirectory(UpdateDepotPaths.GetVersionDirectory("stable", "1.8.9", root));
            Directory.CreateDirectory(Path.Combine(UpdateDepotPaths.GetChannelDirectory("stable", root), ".staging-abc"));

            var latest = UpdateDepotPaths.TryFindLatestVersionDirectory("stable", root);
            Assert.NotNull(latest);
            Assert.Equal("1.9.2", Path.GetFileName(latest));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}

public sealed class UpdateDepotServiceTests
{
    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
        public DateTime UtcNow { get; }
        public DateTime Now => UtcNow.ToLocalTime();
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubManifestVerifier(ReleaseManifest manifest) : IReleaseManifestVerifier
    {
        public ReleaseManifest VerifyAndDeserialize(ReadOnlySpan<byte> manifestJsonUtf8, ReadOnlySpan<byte> detachedSignature) =>
            manifest;
    }

    private sealed class FailingManifestVerifier : IReleaseManifestVerifier
    {
        public ReleaseManifest VerifyAndDeserialize(ReadOnlySpan<byte> manifestJsonUtf8, ReadOnlySpan<byte> detachedSignature) =>
            throw new ReleaseManifestVerificationException("bad signature");
    }

    private sealed class RecordingDownloader : IUpdateArtifactDownloader
    {
        public List<(string Url, string Sha, string Dest)> Calls { get; } = [];

        public Task DownloadAndVerifyAsync(
            string downloadUrl,
            string expectedSha256Hex,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((downloadUrl, expectedSha256Hex, destinationPath));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath, Encoding.UTF8.GetBytes("artifact-bytes"));
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Body)> _responses;

        public ScriptedHandler(Dictionary<string, (HttpStatusCode Status, byte[] Body)> responses) =>
            _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (!_responses.TryGetValue(url, out var entry))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new ByteArrayContent([])
                });
            }

            return Task.FromResult(new HttpResponseMessage(entry.Status)
            {
                Content = new ByteArrayContent(entry.Body)
            });
        }
    }

    [Fact]
    public void GetStatus_returns_empty_when_depot_missing()
    {
        using var scope = new TempDataDirScope();
        var service = CreateService(
            new StubHttpClientFactory(new ScriptedHandler([])),
            new StubManifestVerifier(EmptyManifest()),
            new RecordingDownloader());

        var status = service.GetStatus();
        Assert.Equal(UpdateDepotPaths.GetDepotRoot(scope.Root), status.DepotRoot);
        Assert.Empty(status.Channels);
    }

    [Fact]
    public void GetStatus_lists_staged_version_artifacts_and_manifest_metadata()
    {
        using var scope = new TempDataDirScope();
        var versionDir = UpdateDepotPaths.GetVersionDirectory("stable", "1.9.2", scope.Root);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, UpdateDepotPaths.ManifestFileName), """
            {
              "manifestVersion": 1,
              "channel": "stable",
              "publishedUtc": "2026-07-09T10:00:00Z",
              "artifacts": {}
            }
            """);
        File.WriteAllText(Path.Combine(versionDir, UpdateDepotPaths.ManifestSignatureFileName), "sig");
        File.WriteAllBytes(Path.Combine(versionDir, "StacksAtlas.msi"), [1, 2, 3, 4]);

        var service = CreateService(
            new StubHttpClientFactory(new ScriptedHandler([])),
            new StubManifestVerifier(EmptyManifest()),
            new RecordingDownloader());

        var status = service.GetStatus();
        Assert.Single(status.Channels);
        Assert.Equal("stable", status.Channels[0].Channel);
        var entry = Assert.Single(status.Channels[0].Versions);
        Assert.Equal("1.9.2", entry.Version);
        Assert.True(entry.HasManifest);
        Assert.True(entry.HasSignature);
        Assert.Equal(new DateTime(2026, 7, 9, 10, 0, 0, DateTimeKind.Utc), entry.PublishedUtc);
        Assert.Equal("StacksAtlas.msi", Assert.Single(entry.Artifacts).FileName);
        Assert.Equal(4, entry.TotalBytes);
    }

    [Fact]
    public async Task StageAsync_writes_manifest_file_artifacts_and_docker_meta()
    {
        using var scope = new TempDataDirScope();
        var settings = DefaultSettings();
        var manifest = new ReleaseManifest
        {
            ManifestVersion = 1,
            Channel = "stable",
            PublishedUtc = new DateTime(2026, 7, 9, 10, 0, 0, DateTimeKind.Utc),
            Artifacts = new Dictionary<string, ReleaseArtifactEntry>(StringComparer.Ordinal)
            {
                [UpdateArtifactKeys.WinX64Msi] = new()
                {
                    Version = "1.9.2",
                    Url = "https://releases.stacksatlas.com/stable/StacksAtlas.msi",
                    Sha256 = "abc123"
                },
                [UpdateArtifactKeys.LinuxDocker] = new()
                {
                    Version = "1.9.2",
                    Image = "ghcr.io/keatonstacks/stacksatlas:1.9.2",
                    Digest = "sha256:deadbeef"
                }
            }
        };

        var handler = new ScriptedHandler(new Dictionary<string, (HttpStatusCode, byte[])>(StringComparer.OrdinalIgnoreCase)
        {
            [settings.StableManifestUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes("{}")),
            [settings.StableSignatureUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes("sig")),
        });
        var downloader = new RecordingDownloader();
        var service = CreateService(
            new StubHttpClientFactory(handler),
            new StubManifestVerifier(manifest),
            downloader,
            settings);

        var result = await service.StageAsync("stable");

        Assert.True(result.Success);
        Assert.Equal("1.9.2", result.Version);
        Assert.Contains(UpdateArtifactKeys.WinX64Msi, result.StagedArtifactKeys);
        Assert.Contains(UpdateArtifactKeys.LinuxDocker, result.StagedArtifactKeys);

        var finalDir = UpdateDepotPaths.GetVersionDirectory("stable", "1.9.2", scope.Root);
        Assert.True(File.Exists(Path.Combine(finalDir, UpdateDepotPaths.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(finalDir, UpdateDepotPaths.ManifestSignatureFileName)));
        Assert.True(File.Exists(Path.Combine(finalDir, "StacksAtlas.msi")));
        Assert.True(File.Exists(Path.Combine(finalDir, UpdateDepotArtifactFiles.LinuxDockerMetaFileName)));
        Assert.Single(downloader.Calls);
        Assert.Equal("https://releases.stacksatlas.com/stable/StacksAtlas.msi", downloader.Calls[0].Url);

        var status = service.GetStatus();
        Assert.Single(status.Channels[0].Versions);
        Assert.Equal(2, status.Channels[0].Versions[0].Artifacts.Count);
    }

    [Fact]
    public async Task StageAsync_fails_when_manifest_signature_invalid()
    {
        using var scope = new TempDataDirScope();
        var settings = DefaultSettings();
        var handler = new ScriptedHandler(new Dictionary<string, (HttpStatusCode, byte[])>(StringComparer.OrdinalIgnoreCase)
        {
            [settings.StableManifestUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes("{}")),
            [settings.StableSignatureUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes("sig")),
        });

        var service = CreateService(
            new StubHttpClientFactory(handler),
            new FailingManifestVerifier(),
            new RecordingDownloader(),
            settings);

        var result = await service.StageAsync("stable");
        Assert.False(result.Success);
        Assert.Contains("signature", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(UpdateDepotPaths.GetDepotRoot(scope.Root))
            && Directory.GetDirectories(UpdateDepotPaths.GetDepotRoot(scope.Root), "*", SearchOption.AllDirectories)
                .Any(d => !Path.GetFileName(d).StartsWith(".", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StageAsync_cleans_partial_dir_when_download_fails()
    {
        using var scope = new TempDataDirScope();
        var settings = DefaultSettings();
        var manifest = new ReleaseManifest
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
                    Sha256 = "abc123"
                }
            }
        };
        var handler = new ScriptedHandler(new Dictionary<string, (HttpStatusCode, byte[])>(StringComparer.OrdinalIgnoreCase)
        {
            [settings.StableManifestUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes("{}")),
            [settings.StableSignatureUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes("sig")),
        });

        var service = CreateService(
            new StubHttpClientFactory(handler),
            new StubManifestVerifier(manifest),
            new ThrowingDownloader(),
            settings);

        var result = await service.StageAsync("stable");
        Assert.False(result.Success);
        Assert.False(Directory.Exists(UpdateDepotPaths.GetVersionDirectory("stable", "1.9.2", scope.Root)));

        var channelDir = UpdateDepotPaths.GetChannelDirectory("stable", scope.Root);
        if (Directory.Exists(channelDir))
        {
            Assert.DoesNotContain(
                Directory.GetDirectories(channelDir),
                d => Path.GetFileName(d).StartsWith(".staging-", StringComparison.Ordinal));
        }
    }

    private sealed class ThrowingDownloader : IUpdateArtifactDownloader
    {
        public Task DownloadAndVerifyAsync(
            string downloadUrl,
            string expectedSha256Hex,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("download failed");
    }

    private static UpdateDepotService CreateService(
        IHttpClientFactory httpClientFactory,
        IReleaseManifestVerifier verifier,
        IUpdateArtifactDownloader downloader,
        UpdateSettings? settings = null) =>
        new(
            new FixedClock(new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc)),
            httpClientFactory,
            verifier,
            downloader,
            Options.Create(settings ?? DefaultSettings()),
            NullLogger<UpdateDepotService>.Instance);

    private static UpdateSettings DefaultSettings() => new()
    {
        DefaultChannel = "stable",
        StableManifestUrl = "https://releases.stacksatlas.com/stable/manifest.json",
        StableSignatureUrl = "https://releases.stacksatlas.com/stable/manifest.json.sig",
        PreviewManifestUrl = "https://releases.stacksatlas.com/preview/manifest.json",
        PreviewSignatureUrl = "https://releases.stacksatlas.com/preview/manifest.json.sig"
    };

    private static ReleaseManifest EmptyManifest() => new()
    {
        ManifestVersion = 1,
        Channel = "stable",
        PublishedUtc = DateTime.UtcNow,
        Artifacts = new Dictionary<string, ReleaseArtifactEntry>(StringComparer.Ordinal)
    };

    private sealed class TempDataDirScope : IDisposable
    {
        private readonly string? _previous;

        public string Root { get; }

        public TempDataDirScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "sa-depot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            _previous = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", Root);
            ResetPlatformPathsCache();
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", _previous);
            ResetPlatformPathsCache();
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }

    private static void ResetPlatformPathsCache()
    {
        var field = typeof(PlatformPaths).GetField("_baseDataDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        field!.SetValue(null, null);
    }
}
