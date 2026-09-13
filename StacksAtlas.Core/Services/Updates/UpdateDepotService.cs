using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Updates;

/// <summary>Hub update depot: filesystem status + CDN stage into local cache.</summary>
public sealed class UpdateDepotService(
    IClock clock,
    IHttpClientFactory httpClientFactory,
    IReleaseManifestVerifier manifestVerifier,
    IUpdateArtifactDownloader artifactDownloader,
    IOptions<UpdateSettings> updateSettings,
    ILogger<UpdateDepotService> logger) : IUpdateDepotService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly UpdateSettings _settings = updateSettings.Value;

    public UpdateDepotStatus GetStatus()
    {
        var root = UpdateDepotPaths.GetDepotRoot();
        if (!Directory.Exists(root))
        {
            return new UpdateDepotStatus
            {
                DepotRoot = root,
                Channels = [],
                QueriedUtc = clock.UtcNow
            };
        }

        var channels = new List<UpdateDepotChannelStatus>();
        foreach (var channelDir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var channelName = Path.GetFileName(channelDir);
            if (string.IsNullOrWhiteSpace(channelName) || channelName.StartsWith(".", StringComparison.Ordinal))
                continue;

            var versions = new List<UpdateDepotVersionEntry>();
            foreach (var versionDir in Directory.GetDirectories(channelDir).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var versionName = Path.GetFileName(versionDir);
                if (string.IsNullOrWhiteSpace(versionName) || versionName.StartsWith(".", StringComparison.Ordinal))
                    continue;

                versions.Add(ReadVersionEntry(channelName, versionName, versionDir));
            }

            channels.Add(new UpdateDepotChannelStatus
            {
                Channel = channelName,
                Versions = versions
            });
        }

        return new UpdateDepotStatus
        {
            DepotRoot = root,
            Channels = channels,
            QueriedUtc = clock.UtcNow
        };
    }

    public async Task<UpdateDepotStageResult> StageAsync(
        string? channel = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedChannel = UpdateDepotPaths.NormalizeChannel(channel);

        string manifestUrl;
        string signatureUrl;
        try
        {
            (manifestUrl, signatureUrl) = _settings.ResolveEndpoints(resolvedChannel);
        }
        catch (ArgumentException ex)
        {
            return Fail(resolvedChannel, ex.Message);
        }

        if (!IsAllowedManifestUrl(manifestUrl) || !IsAllowedManifestUrl(signatureUrl))
        {
            logger.LogWarning("Hub depot stage blocked: manifest URL not in configured allowlist.");
            return Fail(resolvedChannel, "Update channel endpoints are not configured.");
        }

        byte[] manifestBytes;
        byte[] signatureBytes;
        try
        {
            var client = httpClientFactory.CreateClient(nameof(UpdateCheckService));
            using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            using var signatureRequest = new HttpRequestMessage(HttpMethod.Get, signatureUrl);
            var manifestTask = client.SendAsync(manifestRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var signatureTask = client.SendAsync(signatureRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
            await Task.WhenAll(manifestTask, signatureTask);

            var manifestResponse = await manifestTask;
            var signatureResponse = await signatureTask;
            if (!manifestResponse.IsSuccessStatusCode || !signatureResponse.IsSuccessStatusCode)
            {
                return Fail(
                    resolvedChannel,
                    resolvedChannel == "stable"
                        ? "No stable release has been published yet."
                        : "No preview release is available on the update server right now.");
            }

            manifestBytes = await manifestResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            signatureBytes = await signatureResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hub depot: failed to fetch release manifest for {Channel}", resolvedChannel);
            return Fail(resolvedChannel, "Failed to download the release manifest.");
        }

        ReleaseManifest manifest;
        try
        {
            manifest = manifestVerifier.VerifyAndDeserialize(manifestBytes, signatureBytes);
        }
        catch (ReleaseManifestVerificationException ex)
        {
            logger.LogWarning(ex, "Hub depot: manifest verification failed for {Channel}", resolvedChannel);
            return Fail(resolvedChannel, "Release manifest signature verification failed.");
        }

        var version = ResolveReleaseVersion(manifest);
        if (string.IsNullOrWhiteSpace(version))
            return Fail(resolvedChannel, "Release manifest does not contain a usable artifact version.");

        var finalDir = UpdateDepotPaths.GetVersionDirectory(resolvedChannel, version);
        var channelDir = UpdateDepotPaths.GetChannelDirectory(resolvedChannel);
        Directory.CreateDirectory(channelDir);

        var alreadyStaged = Directory.Exists(finalDir)
            || (TryGetLatestStagedRelease(resolvedChannel, out var existingVersion, out _, out _)
                && string.Equals(existingVersion, version, StringComparison.OrdinalIgnoreCase));

        var stagingDir = Path.Combine(channelDir, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        var stagedKeys = new List<string>();
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(stagingDir, UpdateDepotPaths.ManifestFileName),
                manifestBytes,
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(stagingDir, UpdateDepotPaths.ManifestSignatureFileName),
                signatureBytes,
                cancellationToken);

            foreach (var (key, artifact) in manifest.Artifacts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var fileName = UpdateDepotArtifactFiles.ResolveFileName(key);
                if (fileName is null)
                {
                    logger.LogWarning("Hub depot: skipping unknown artifact key {ArtifactKey}", key);
                    continue;
                }

                var destPath = Path.Combine(stagingDir, fileName);
                if (artifact.IsDockerArtifact)
                {
                    var meta = JsonSerializer.Serialize(new
                    {
                        artifactKey = key,
                        version = artifact.Version,
                        image = artifact.Image,
                        digest = artifact.Digest,
                        criticality = artifact.Criticality
                    }, WriteJsonOptions);
                    await File.WriteAllTextAsync(destPath, meta, Encoding.UTF8, cancellationToken);
                    stagedKeys.Add(key);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(artifact.Url) || string.IsNullOrWhiteSpace(artifact.Sha256))
                {
                    logger.LogWarning("Hub depot: skipping incomplete file artifact {ArtifactKey}", key);
                    continue;
                }

                await artifactDownloader.DownloadAndVerifyAsync(
                    artifact.Url,
                    artifact.Sha256,
                    destPath,
                    cancellationToken);
                stagedKeys.Add(key);
            }

            if (stagedKeys.Count == 0)
            {
                TryDeleteDirectory(stagingDir);
                return Fail(resolvedChannel, "Release manifest contained no stageable artifacts.");
            }

            if (Directory.Exists(finalDir))
                Directory.Delete(finalDir, recursive: true);

            Directory.Move(stagingDir, finalDir);

            logger.LogInformation(
                "Hub depot staged {Channel}/{Version} with {Count} artifacts at {Path}",
                resolvedChannel,
                version,
                stagedKeys.Count,
                finalDir);

            return new UpdateDepotStageResult
            {
                Success = true,
                Channel = resolvedChannel,
                Version = version,
                Message = alreadyStaged
                    ? $"Re-staged {resolvedChannel} v{version} (same version was already in the depot; artifacts refreshed)."
                    : $"Staged {stagedKeys.Count} artifact(s) for {resolvedChannel} v{version}.",
                Path = finalDir,
                StagedArtifactKeys = stagedKeys,
                CompletedUtc = clock.UtcNow,
                AlreadyStaged = alreadyStaged
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub depot stage failed for {Channel}/{Version}", resolvedChannel, version);
            TryDeleteDirectory(stagingDir);
            return Fail(resolvedChannel, "Failed to stage update artifacts into the Hub depot.", version);
        }
    }

    public bool TryGetLatestStagedRelease(
        string? channel,
        out string version,
        out ReleaseManifest manifest,
        out string versionDirectory)
    {
        version = "";
        manifest = null!;
        versionDirectory = "";

        var resolvedChannel = UpdateDepotPaths.NormalizeChannel(channel);
        var latestDir = UpdateDepotPaths.TryFindLatestVersionDirectory(resolvedChannel);
        if (latestDir is null)
            return false;

        var manifestPath = Path.Combine(latestDir, UpdateDepotPaths.ManifestFileName);
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            var json = File.ReadAllText(manifestPath);
            var parsed = JsonSerializer.Deserialize<ReleaseManifest>(json, ManifestJsonOptions);
            if (parsed is null)
                return false;

            var versionName = Path.GetFileName(latestDir);
            if (string.IsNullOrWhiteSpace(versionName) || !SemverUtility.TryParse(versionName, out _))
                return false;

            version = versionName;
            manifest = parsed;
            versionDirectory = latestDir;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hub depot: failed to read staged manifest for {Channel}", resolvedChannel);
            return false;
        }
    }

    public bool TryResolveArtifactPath(string? channel, string version, string artifactKey, out string path) =>
        UpdateDepotPaths.TryResolveArtifactPath(
            UpdateDepotPaths.NormalizeChannel(channel),
            version,
            artifactKey,
            out path);

    private UpdateDepotVersionEntry ReadVersionEntry(string channel, string version, string versionDir)
    {
        var manifestPath = Path.Combine(versionDir, UpdateDepotPaths.ManifestFileName);
        var signaturePath = Path.Combine(versionDir, UpdateDepotPaths.ManifestSignatureFileName);
        var hasManifest = File.Exists(manifestPath);
        var hasSignature = File.Exists(signaturePath);

        DateTime? publishedUtc = null;
        if (hasManifest)
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ReleaseManifest>(json, ManifestJsonOptions);
                publishedUtc = manifest?.PublishedUtc;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Hub depot: failed to read manifest for {Channel}/{Version}", channel, version);
            }
        }

        var artifacts = new List<UpdateDepotArtifactEntry>();
        long totalBytes = 0;
        foreach (var file in Directory.GetFiles(versionDir))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, UpdateDepotPaths.ManifestFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, UpdateDepotPaths.ManifestSignatureFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var info = new FileInfo(file);
            artifacts.Add(new UpdateDepotArtifactEntry
            {
                FileName = name,
                SizeBytes = info.Length
            });
            totalBytes += info.Length;
        }

        return new UpdateDepotVersionEntry
        {
            Version = version,
            Path = versionDir,
            HasManifest = hasManifest,
            HasSignature = hasSignature,
            PublishedUtc = publishedUtc,
            Artifacts = artifacts.OrderBy(a => a.FileName, StringComparer.OrdinalIgnoreCase).ToList(),
            TotalBytes = totalBytes
        };
    }

    private UpdateDepotStageResult Fail(string channel, string message, string? version = null) =>
        new()
        {
            Success = false,
            Channel = channel,
            Version = version,
            Message = message,
            Path = null,
            StagedArtifactKeys = [],
            CompletedUtc = clock.UtcNow
        };

    private bool IsAllowedManifestUrl(string url)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            _settings.StableManifestUrl,
            _settings.StableSignatureUrl,
            _settings.PreviewManifestUrl,
            _settings.PreviewSignatureUrl
        };
        return allowed.Contains(url);
    }

    private static string? ResolveReleaseVersion(ReleaseManifest manifest)
    {
        foreach (var artifact in manifest.Artifacts.Values)
        {
            if (!string.IsNullOrWhiteSpace(artifact.Version) && SemverUtility.TryParse(artifact.Version, out _))
                return artifact.Version.Trim();
        }

        return null;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of partial staging dirs.
        }
    }
}
