using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace StacksAtlas.Core.Services.Integrations;

/// <summary>
/// Passive identification of OpenAVC room-control hosts during network scan.
/// OpenAVC listens on 8080/8443 and exposes unauthenticated /api/health (see OpenAVC IT network guide).
/// </summary>
public static class OpenAvcHostProbe
{
    private static readonly HttpClient Client = CreateClient();

    public sealed record Match(string Version, string BaseUrl);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 2,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2.5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StacksAtlas/1.8 OpenAVC-Discovery");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static bool LooksLikeOpenAvcTitle(string? httpTitle) =>
        !string.IsNullOrWhiteSpace(httpTitle)
        && httpTitle.Contains("OpenAVC", StringComparison.OrdinalIgnoreCase);

    public static async Task<Match?> ProbeAsync(string ip, int port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ip) || port <= 0)
            return null;

        var schemes = port is 443 or 8443 ? new[] { "https", "http" } : new[] { "http", "https" };
        foreach (var scheme in schemes)
        {
            var match = await TryHealthEndpointAsync(scheme, ip, port, cancellationToken);
            if (match != null)
                return match;

            if (await LooksLikeOpenAvcApiAsync(scheme, ip, port, cancellationToken))
                return new Match("unknown", $"{scheme}://{ip}:{port}");
        }

        return null;
    }

    public static bool TryParseHealthJson(string body, out string? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(body) || body.TrimStart().StartsWith('<'))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("version", out var versionEl) || versionEl.ValueKind != JsonValueKind.String)
                return false;

            version = versionEl.GetString();
            if (string.IsNullOrWhiteSpace(version))
                return false;

            return root.TryGetProperty("status", out _)
                || root.TryGetProperty("uptime", out _)
                || root.TryGetProperty("hostname", out _);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Match?> TryHealthEndpointAsync(
        string scheme,
        string ip,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{scheme}://{ip}:{port}/api/health");
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!TryParseHealthJson(body, out var version) || version == null)
                return null;

            return new Match(version, $"{scheme}://{ip}:{port}");
        }
        catch
        {
            return null;
        }
    }

  private static async Task<bool> LooksLikeOpenAvcApiAsync(
        string scheme,
        string ip,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{scheme}://{ip}:{port}/api/devices");
            using var response = await Client.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return false;

            return response.Headers.WwwAuthenticate.Any(h =>
                h.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
