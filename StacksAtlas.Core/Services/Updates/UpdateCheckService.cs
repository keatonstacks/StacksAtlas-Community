using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Updates;

public sealed class UpdateCheckService(
    IHttpClientFactory httpClientFactory,
    IApplianceVersionProvider versionProvider,
    IReleaseManifestVerifier manifestVerifier,
    IOptions<UpdateSettings> updateSettings,
    ILogger<UpdateCheckService> logger) : IUpdateCheckService
{
    private readonly UpdateSettings _settings = updateSettings.Value;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        string? channel = null,
        CancellationToken cancellationToken = default,
        string? httpClientName = null)
    {
        var current = versionProvider.GetCurrent();
        var resolvedChannel = string.IsNullOrWhiteSpace(channel) ? current.Channel : channel.Trim().ToLowerInvariant();

        string manifestUrl;
        string signatureUrl;
        try
        {
            (manifestUrl, signatureUrl) = _settings.ResolveEndpoints(resolvedChannel);
        }
        catch (ArgumentException ex)
        {
            return Failure(current, resolvedChannel, UpdateCheckStatuses.CheckFailed, ex.Message);
        }

        if (!IsAllowedManifestUrl(manifestUrl) || !IsAllowedManifestUrl(signatureUrl))
        {
            logger.LogWarning("Blocked update check  -  manifest URL not in configured allowlist.");
            return Failure(
                current,
                resolvedChannel,
                UpdateCheckStatuses.CheckFailed,
                "Update channel endpoints are not configured.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(httpClientName ?? nameof(UpdateCheckService));
            using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            using var signatureRequest = new HttpRequestMessage(HttpMethod.Get, signatureUrl);

            var manifestTask = client.SendAsync(manifestRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var signatureTask = client.SendAsync(signatureRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
            await Task.WhenAll(manifestTask, signatureTask);

            var manifestResponse = await manifestTask;
            var signatureResponse = await signatureTask;

            if (!manifestResponse.IsSuccessStatusCode || !signatureResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Update manifest fetch failed. ManifestStatus={ManifestStatus} SignatureStatus={SignatureStatus}",
                    (int)manifestResponse.StatusCode,
                    (int)signatureResponse.StatusCode);

                var unavailableMessage = resolvedChannel == "stable"
                    ? "No stable release has been published yet. Enable preview below if you are on the beta program."
                    : "No preview release is available on the update server right now.";

                return Failure(current, resolvedChannel, UpdateCheckStatuses.Unavailable, unavailableMessage);
            }

            var manifestBytes = await manifestResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            var signatureBytes = await signatureResponse.Content.ReadAsByteArrayAsync(cancellationToken);

            var manifest = manifestVerifier.VerifyAndDeserialize(manifestBytes, signatureBytes);

            if (!manifest.Artifacts.TryGetValue(current.ArtifactKey, out var artifact))
            {
                return WithApplyHints(new UpdateCheckResult(
                    Status: UpdateCheckStatuses.Unavailable,
                    UpdateAvailable: false,
                    CurrentVersion: current.Version,
                    Channel: resolvedChannel,
                    ArtifactKey: current.ArtifactKey,
                    AvailableVersion: null,
                    DownloadUrl: null,
                    Sha256: null,
                    Image: null,
                    Digest: null,
                    Criticality: null,
                    ReleaseNotesUrl: manifest.ReleaseNotesUrl,
                    PublishedUtc: manifest.PublishedUtc,
                    Message: "No update is published for this platform in the current release."));
            }

            if (!string.IsNullOrWhiteSpace(artifact.MinUpgradeFrom))
            {
                if (!SemverUtility.TryCompare(current.Version, artifact.MinUpgradeFrom, out var minCmp))
                {
                    return WithApplyHints(new UpdateCheckResult(
                        Status: UpdateCheckStatuses.CheckFailed,
                        UpdateAvailable: false,
                        CurrentVersion: current.Version,
                        Channel: resolvedChannel,
                        ArtifactKey: current.ArtifactKey,
                        AvailableVersion: artifact.Version,
                        DownloadUrl: artifact.Url,
                        Sha256: artifact.Sha256,
                        Image: artifact.Image,
                        Digest: artifact.Digest,
                        Criticality: artifact.Criticality,
                        ReleaseNotesUrl: manifest.ReleaseNotesUrl,
                        PublishedUtc: manifest.PublishedUtc,
                        Message: $"Could not compare versions (current={current.Version}, minUpgradeFrom={artifact.MinUpgradeFrom})."));
                }

                if (minCmp < 0)
                {
                    return WithApplyHints(new UpdateCheckResult(
                        Status: UpdateCheckStatuses.CheckFailed,
                        UpdateAvailable: false,
                        CurrentVersion: current.Version,
                        Channel: resolvedChannel,
                        ArtifactKey: current.ArtifactKey,
                        AvailableVersion: artifact.Version,
                        DownloadUrl: artifact.Url,
                        Sha256: artifact.Sha256,
                        Image: artifact.Image,
                        Digest: artifact.Digest,
                        Criticality: artifact.Criticality,
                        ReleaseNotesUrl: manifest.ReleaseNotesUrl,
                        PublishedUtc: manifest.PublishedUtc,
                        Message: $"Current version {current.Version} is below minimum {artifact.MinUpgradeFrom} for this release."));
                }
            }

            if (!SemverUtility.TryCompare(artifact.Version, current.Version, out var comparison))
            {
                return WithApplyHints(new UpdateCheckResult(
                    Status: UpdateCheckStatuses.CheckFailed,
                    UpdateAvailable: false,
                    CurrentVersion: current.Version,
                    Channel: resolvedChannel,
                    ArtifactKey: current.ArtifactKey,
                    AvailableVersion: artifact.Version,
                    DownloadUrl: artifact.Url,
                    Sha256: artifact.Sha256,
                    Image: artifact.Image,
                    Digest: artifact.Digest,
                    Criticality: artifact.Criticality,
                    ReleaseNotesUrl: manifest.ReleaseNotesUrl,
                    PublishedUtc: manifest.PublishedUtc,
                    Message: $"Could not compare versions (current={current.Version}, available={artifact.Version})."));
            }

            var updateAvailable = comparison > 0;
            var status = updateAvailable ? UpdateCheckStatuses.UpdateAvailable : UpdateCheckStatuses.UpToDate;
            var message = updateAvailable
                ? $"Update {artifact.Version} is available."
                : comparison == 0
                    ? $"You are running the latest published release (v{current.Version})."
                    : $"You are on v{current.Version}, ahead of the published {resolvedChannel} release (v{artifact.Version}).";

            return WithApplyHints(new UpdateCheckResult(
                Status: status,
                UpdateAvailable: updateAvailable,
                CurrentVersion: current.Version,
                Channel: resolvedChannel,
                ArtifactKey: current.ArtifactKey,
                AvailableVersion: artifact.Version,
                DownloadUrl: artifact.Url,
                Sha256: artifact.Sha256,
                Image: artifact.Image,
                Digest: artifact.Digest,
                Criticality: artifact.Criticality,
                ReleaseNotesUrl: manifest.ReleaseNotesUrl,
                PublishedUtc: manifest.PublishedUtc,
                Message: message));
        }
        catch (ReleaseManifestVerificationException ex)
        {
            logger.LogWarning(ex, "Signed manifest verification failed during update check.");
            return Failure(
                current,
                resolvedChannel,
                UpdateCheckStatuses.CheckFailed,
                "Update metadata could not be verified. Rebuild with the matching release or contact your administrator.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Update manifest fetch error.");
            var timedOut = ex is TaskCanceledException or TimeoutException
                || ex.InnerException is TimeoutException or TaskCanceledException;
            var message = timedOut
                ? "The update server did not respond in time. Try Check for Updates again, then Download & Install."
                : "Could not reach the update server. Check internet connectivity and DNS for releases.stacksatlas.com.";
            return Failure(
                current,
                resolvedChannel,
                UpdateCheckStatuses.CheckFailed,
                message);
        }
    }

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

    private static UpdateCheckResult Failure(
        ApplianceVersionInfo current,
        string channel,
        string status,
        string message) =>
        WithApplyHints(new UpdateCheckResult(
            Status: status,
            UpdateAvailable: false,
            CurrentVersion: current.Version,
            Channel: channel,
            ArtifactKey: current.ArtifactKey,
            AvailableVersion: null,
            DownloadUrl: null,
            Sha256: null,
            Image: null,
            Digest: null,
            Criticality: null,
            ReleaseNotesUrl: null,
            PublishedUtc: null,
            Message: message));

    private static UpdateCheckResult WithApplyHints(UpdateCheckResult result)
    {
        var (applySupported, applyMode) = UpdateApplyCapabilities.Resolve(result.ArtifactKey, result.DownloadUrl);
        return result with { ApplySupported = applySupported, ApplyMode = applyMode };
    }
}
