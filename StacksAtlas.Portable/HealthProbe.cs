using System.Diagnostics;
using System.Net;

namespace StacksAtlas.Portable;

internal static class HealthProbe
{
    public static async Task<bool> WaitForApiAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await TryHttpClientProbeAsync(cancellationToken))
                return true;

            if (TryCurlProbe())
                return true;

            await Task.Delay(1000, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> TryHttpClientProbeAsync(CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = static (_, _, _, _) => true,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };

        var urls = new[]
        {
            "https://127.0.0.1:5001/api/health",
            "https://localhost:5001/api/health",
            "http://127.0.0.1:5000/api/health"
        };

        foreach (var url in urls)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (IsHealthyResponse(response.StatusCode))
                {
                    PortableLog.Write($"API ready at {url} ({(int)response.StatusCode})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                PortableLog.Write($"Health probe failed for {url}: {ex.Message}");
            }
        }

        return false;
    }

    private static bool TryCurlProbe()
    {
        foreach (var uri in new[]
        {
            "https://127.0.0.1:5001/api/health",
            "https://localhost:5001/api/health",
            "http://127.0.0.1:5000/api/health"
        })
        {
            try
            {
                var args = new List<string> { "-s", "-o", "NUL", "-w", "%{http_code}" };
                if (uri.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                    args.Insert(0, "-k");

                args.Add(uri);
                var code = RunCurl(args);
                if (code is "200" or "204")
                {
                    PortableLog.Write($"API ready at {uri} (curl HTTP {code})");
                    return true;
                }

                if (!string.IsNullOrEmpty(code))
                    PortableLog.Write($"curl probe {uri} returned HTTP {code}");
            }
            catch (Exception ex)
            {
                PortableLog.Write($"curl probe failed for {uri}: {ex.Message}");
            }
        }

        return false;
    }

    private static string? RunCurl(IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo);
        if (process is null)
            return null;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(5000);
        return output;
    }

    private static bool IsHealthyResponse(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.OK or HttpStatusCode.NoContent
            or HttpStatusCode.Redirect or HttpStatusCode.Moved
            or HttpStatusCode.RedirectKeepVerb or HttpStatusCode.TemporaryRedirect;
}
