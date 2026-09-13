using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Updates;

public interface IUpdateArtifactDownloader
{
    Task DownloadAndVerifyAsync(
        string downloadUrl,
        string expectedSha256Hex,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed class UpdateArtifactDownloader(
    IHttpClientFactory httpClientFactory,
    ILogger<UpdateArtifactDownloader> logger,
    FederationTransportHelper? transportHelper = null,
    HubEndpointResolver? hubEndpointResolver = null,
    NodeEnrollmentClient? enrollmentClient = null,
    FederationSettingsStore? federationSettings = null) : IUpdateArtifactDownloader
{
    private static readonly HashSet<string> AllowedCdnHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "releases.stacksatlas.com"
    };

    public async Task DownloadAndVerifyAsync(
        string downloadUrl,
        string expectedSha256Hex,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Download URL must be HTTPS.");
        }

        var isHubArtifact = IsHubFederationArtifactUrl(uri);
        if (!isHubArtifact && !AllowedCdnHosts.Contains(uri.Host))
            throw new InvalidOperationException("Download URL is not from an allowed release host.");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var tempPath = destinationPath + ".partial";

        if (File.Exists(tempPath))
            File.Delete(tempPath);

        logger.LogInformation("Downloading update artifact from {Host}{Path}...", uri.Host, uri.AbsolutePath);

        if (isHubArtifact)
        {
            await DownloadFromHubAsync(uri, tempPath, cancellationToken);
        }
        else
        {
            var client = httpClientFactory.CreateClient(nameof(UpdateApplyService));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(tempPath);
            await input.CopyToAsync(output, cancellationToken);
        }

        var actualHash = ComputeSha256Hex(tempPath);
        var expected = expectedSha256Hex.Trim().ToLowerInvariant();
        if (!string.Equals(actualHash, expected, StringComparison.Ordinal))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException(
                "Downloaded artifact failed SHA-256 verification. The update was not applied.");
        }

        if (File.Exists(destinationPath))
            File.Delete(destinationPath);

        File.Move(tempPath, destinationPath);
        logger.LogInformation("Update artifact verified and staged at {Path}", destinationPath);
    }

    private async Task DownloadFromHubAsync(Uri uri, string tempPath, CancellationToken cancellationToken)
    {
        if (transportHelper is null || hubEndpointResolver is null || enrollmentClient is null || federationSettings is null)
            throw new InvalidOperationException("Hub artifact download requires federation transport on Node appliances.");

        var (clientCert, hubRoot) = enrollmentClient.LoadCertificates();
        if (clientCert is null || hubRoot is null)
            throw new InvalidOperationException("Hub mTLS certificates are not available for artifact download.");

        var plan = hubEndpointResolver.Resolve(useMtls: true, path: uri.PathAndQuery);
        if (plan.Candidates.Count == 0)
            throw new InvalidOperationException("No Hub endpoint candidates available for artifact download.");

        var candidate = plan.Candidates[0];
        using var client = transportHelper.CreateHttpClient(plan, candidate, clientCert, hubRoot);
        client.Timeout = TimeSpan.FromMinutes(30);
        var downloadUrl = plan.BuildUrl(candidate);
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(tempPath);
        await input.CopyToAsync(output, cancellationToken);
    }

    public static bool TryValidateDownloadUrl(string downloadUrl, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsed))
            return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (IsHubFederationArtifactUrl(parsed))
        {
            uri = parsed;
            return true;
        }
        if (!AllowedCdnHosts.Contains(parsed.Host))
            return false;
        uri = parsed;
        return true;
    }

    public static bool IsHubFederationArtifactUrl(Uri uri) =>
        uri.AbsolutePath.Contains("/api/federation/updates/artifacts/", StringComparison.OrdinalIgnoreCase);

    private static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
