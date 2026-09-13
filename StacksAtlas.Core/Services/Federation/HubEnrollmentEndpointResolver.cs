using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Federation;

/// <summary>
/// Resolves Hub enrollment / Direct Push endpoints. Tailscale is opt-in  -  LAN is the default.
/// </summary>
public static class HubEnrollmentEndpointResolver
{
    public const int MtlsPort = 5002;

    public static string ResolveHubBaseUrl(
        FederationSettings settings,
        string? requestScheme,
        string? requestHost,
        Func<string> getLocalIpAddress)
    {
        var requestUrl = settings.HubUrl;
        if (string.IsNullOrWhiteSpace(requestUrl) &&
            !string.IsNullOrWhiteSpace(requestScheme) &&
            !string.IsNullOrWhiteSpace(requestHost))
        {
            requestUrl = $"{requestScheme}://{requestHost}";
        }

        if (string.IsNullOrWhiteSpace(requestUrl))
            requestUrl = "https://localhost:5001";

        if (requestUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            requestUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            requestUrl.Contains("[::1]", StringComparison.OrdinalIgnoreCase))
        {
            var localIp = getLocalIpAddress();
            if (!string.Equals(localIp, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                requestUrl = requestUrl
                    .Replace("localhost", localIp, StringComparison.OrdinalIgnoreCase)
                    .Replace("127.0.0.1", localIp, StringComparison.OrdinalIgnoreCase)
                    .Replace("[::1]", localIp, StringComparison.OrdinalIgnoreCase);
            }
        }

        return requestUrl;
    }

    public static string ResolveLanHost(FederationSettings settings, string hubBaseUrl)
    {
        _ = settings;
        return new Uri(hubBaseUrl).Host;
    }

    public static string? ResolveTailscaleHost(FederationSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.HubTailscaleMagicDns))
            return settings.HubTailscaleMagicDns.Trim().TrimEnd('.');

        if (!string.IsNullOrWhiteSpace(settings.HubTailscaleIpv4))
            return settings.HubTailscaleIpv4.Trim();

        return null;
    }

    public static string BuildEnrollmentConnectionString(string host, string token, int mtlsPort = MtlsPort) =>
        $"sa-enroll://{host}:{mtlsPort}?token={Uri.EscapeDataString(token)}";

    /// <summary>Hub REST bootstrap URL (port 5001) used during mTLS CSR enrollment.</summary>
    public static string BuildBootstrapEnrollUrl(string host) =>
        $"https://{host}:5001/api/federation/enroll";

    public static string ResolveDirectPushHubUrl(FederationSettings settings, string? requestScheme, string? requestHost, Func<string> getLocalIpAddress)
    {
        var hubUrl = ResolveHubBaseUrl(settings, requestScheme, requestHost, getLocalIpAddress);

        if (OperatingSystem.IsWindows())
        {
            if (hubUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                hubUrl = "https://" + hubUrl[7..];
            hubUrl = hubUrl.Replace(":5000", ":5001", StringComparison.OrdinalIgnoreCase);
        }

        return hubUrl;
    }
}
