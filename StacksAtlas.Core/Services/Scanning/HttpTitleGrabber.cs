using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Scanning;

public partial class HttpTitleGrabber
{
    private readonly ILogger<HttpTitleGrabber> _logger;
    private static readonly global::System.Net.Http.HttpClient _httpClient;
    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, (string? Title, global::System.DateTime LastGrabbed)> _cache = new();
    private static readonly global::System.TimeSpan CacheTtl = global::System.TimeSpan.FromMinutes(30);

    static HttpTitleGrabber()
    {
        var handler = new global::System.Net.Http.HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            UseCookies = false,
            AutomaticDecompression = global::System.Net.DecompressionMethods.GZip | global::System.Net.DecompressionMethods.Deflate
        };

        _httpClient = new global::System.Net.Http.HttpClient(handler)
        {
            Timeout = global::System.TimeSpan.FromSeconds(3)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (StacksAtlas/1.2; IndustrialDiscovery)");
        _httpClient.DefaultRequestHeaders.CacheControl = new global::System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
    }

    public HttpTitleGrabber(ILogger<HttpTitleGrabber> logger)
    {
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<string?> GetTitleAsync(string ip, int port, global::System.Threading.CancellationToken token = default)
    {
        string primaryScheme = IsLikelyHttps(port) ? "https" : "http";
        
        // 1. Check Cache
        string cacheKey = $"{ip}:{port}";
        if (_cache.TryGetValue(cacheKey, out var cached) && (global::System.DateTime.UtcNow - cached.LastGrabbed) < CacheTtl)
        {
            return cached.Title;
        }

        var result = await TryGetTitleInternalAsync(primaryScheme, ip, port, token);

        if (result == null && !IsStandardPort(port))
        {
            string secondaryScheme = primaryScheme == "https" ? "http" : "https";
            _logger.LogDebug("Primary scheme {Primary} failed for {Ip}:{Port}. Retrying with {Secondary}...", primaryScheme, ip, port, secondaryScheme);
            result = await TryGetTitleInternalAsync(secondaryScheme, ip, port, token);
        }

        // 2. Update Cache (even if null, to prevent constant retries on non-web devices)
        _cache[cacheKey] = (result, global::System.DateTime.UtcNow);

        return result;
    }

    private async global::System.Threading.Tasks.Task<string?> TryGetTitleInternalAsync(string scheme, string ip, int port, global::System.Threading.CancellationToken token)
    {
        try
        {
            string url = $"{scheme}://{ip}:{port}/";

            using var response = await _httpClient.GetAsync(url, global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType;
            var mediaType = contentType?.MediaType;
            if (mediaType != null && !mediaType.Contains("html") && !mediaType.Contains("xml") && !mediaType.Contains("text"))
                return null;

            // Detect Charset
            var encoding = global::System.Text.Encoding.UTF8;
            if (!string.IsNullOrEmpty(contentType?.CharSet))
            {
                try 
                { 
                    encoding = global::System.Text.Encoding.GetEncoding(contentType.CharSet); 
                    _logger.LogTrace("Detected charset {Charset} for {Ip}", contentType.CharSet, ip);
                } 
                catch { /* Fallback to UTF8 */ }
            }

            using var stream = await response.Content.ReadAsStreamAsync(token);
            byte[] buffer = new byte[8192];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
            if (bytesRead == 0) return null;

            string content = encoding.GetString(buffer, 0, bytesRead);
            
            var match = TitleRegex().Match(content);
            if (match.Success)
            {
                string title = match.Groups[1].Value.Trim();
                title = global::System.Net.WebUtility.HtmlDecode(title);
                title = global::System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();

                if (IsGarbageTitle(title))
                {
                    _logger.LogTrace("Ignoring generic title '{Title}' from {Ip}", title, ip);
                    return null;
                }

                if (title.Length > 120) title = title.Substring(0, 117) + "...";
                
                _logger.LogDebug("Captured HTTP title for {Ip}:{Port} -> '{Title}'", ip, port, title);
                return title;
            }
        }
        catch (global::System.Exception ex)
        {
            _logger.LogTrace("HTTP Title grab failed for {Scheme}://{Ip}:{Port}. Error: {Message}", scheme, ip, port, ex.Message);
        }

        return null;
    }

    private static bool IsLikelyHttps(int port)
    {
        return port == 443 || port == 8443 || port == 4443 || port == 9443 || port == 5001 || port == 10443;
    }

    private static bool IsStandardPort(int port)
    {
        return port == 80 || port == 443;
    }

    private static bool IsGarbageTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        
        string lower = title.ToLowerInvariant();
        string[] garbage = { "403 forbidden", "404 not found", "401 unauthorized", "index of /", "error", "untitled document", "loading..." };
        
        return garbage.Any(g => lower.Contains(g));
    }

    [global::System.Text.RegularExpressions.GeneratedRegex(@"<title[^>]*>(.*?)</title>", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase | global::System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial global::System.Text.RegularExpressions.Regex TitleRegex();
}
