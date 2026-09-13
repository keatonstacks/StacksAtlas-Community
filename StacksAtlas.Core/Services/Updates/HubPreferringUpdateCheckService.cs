using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Updates;

/// <summary>
/// Node update check: prefer Hub depot when enrolled with mTLS certificates.
/// CDN is used only when the appliance is not Hub-enrolled (no certs / no Hub URL).
/// Enrolled Nodes do not silently fall back to CDN (air-gap / Hub-as-door policy).
/// </summary>
public sealed class HubPreferringUpdateCheckService(
    UpdateCheckService cdnCheckService,
    IApplianceVersionProvider versionProvider,
    HubEndpointResolver hubEndpointResolver,
    FederationTransportHelper transportHelper,
    NodeEnrollmentClient enrollmentClient,
    FederationSettingsStore federationSettings,
    ILogger<HubPreferringUpdateCheckService> logger) : IUpdateCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        string? channel = null,
        CancellationToken cancellationToken = default,
        string? httpClientName = null)
    {
        var current = versionProvider.GetCurrent();
        var resolvedChannel = UpdateDepotPaths.NormalizeChannel(channel ?? current.Channel);

        if (!TryCreateHubClient(out var client, out var checkUrl) || client is null)
        {
            return await cdnCheckService.CheckForUpdatesAsync(channel, cancellationToken, httpClientName);
        }

        try
        {
            using (client)
            {
                var url =
                    $"{checkUrl}?channel={Uri.EscapeDataString(resolvedChannel)}" +
                    $"&version={Uri.EscapeDataString(current.Version)}" +
                    $"&artifactKey={Uri.EscapeDataString(current.ArtifactKey)}";

                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Hub depot update check HTTP {Status}",
                        (int)response.StatusCode);
                    return Failure(
                        current,
                        resolvedChannel,
                        UpdateCheckStatuses.CheckFailed,
                        $"Hub depot update check failed (HTTP {(int)response.StatusCode}).");
                }

                var hubResult = await response.Content.ReadFromJsonAsync<UpdateCheckResult>(JsonOptions, cancellationToken);
                if (hubResult is null)
                {
                    return Failure(
                        current,
                        resolvedChannel,
                        UpdateCheckStatuses.CheckFailed,
                        "Hub depot returned an empty update check response.");
                }

                logger.LogInformation(
                    "Update check served from Hub depot ({Status}, available={Available})",
                    hubResult.Status,
                    hubResult.AvailableVersion);
                return hubResult;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Hub depot update check failed");
            return Failure(
                current,
                resolvedChannel,
                UpdateCheckStatuses.CheckFailed,
                "Could not reach the Hub update depot. Check Hub connectivity (mTLS port 5002).");
        }
    }

    private bool TryCreateHubClient(out HttpClient? client, out string checkUrl)
    {
        client = null;
        checkUrl = "";

        var settings = federationSettings.Current;
        if (string.IsNullOrWhiteSpace(settings.HubUrl)
            && string.IsNullOrWhiteSpace(settings.HubTailscaleMagicDns)
            && string.IsNullOrWhiteSpace(settings.HubTailscaleIpv4))
        {
            return false;
        }

        var (clientCert, hubRoot) = enrollmentClient.LoadCertificates();
        if (clientCert is null || hubRoot is null)
            return false;

        var plan = hubEndpointResolver.Resolve(useMtls: true, path: "/api/federation/updates/check");
        if (plan.Candidates.Count == 0)
            return false;

        var candidate = plan.Candidates[0];
        client = transportHelper.CreateHttpClient(plan, candidate, clientCert, hubRoot);
        client.Timeout = TimeSpan.FromMinutes(2);
        checkUrl = plan.BuildUrl(candidate);
        return true;
    }

    private static UpdateCheckResult Failure(
        ApplianceVersionInfo current,
        string channel,
        string status,
        string message)
    {
        var (applySupported, applyMode) = UpdateApplyCapabilities.Resolve(current.ArtifactKey, null);
        return new UpdateCheckResult(
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
            Message: message,
            ApplySupported: applySupported,
            ApplyMode: applyMode);
    }
}
