using System.DirectoryServices.Protocols;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Auth;

public class SsoDiagnosticService(HttpClient httpClient, ILogger<SsoDiagnosticService> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<SsoDiagnosticService> _logger = logger;

    public async Task<(bool Success, string Message)> TestOidcAsync(OidcSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Authority)) return (false, "Authority URL is required.");

        try
        {
            var baseUrl = settings.Authority.TrimEnd('/');
            var configUrl = $"{baseUrl}/.well-known/openid-configuration";
            
            var response = await _httpClient.GetAsync(configUrl);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Failed to fetch metadata from {configUrl}. HTTP Status: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var issuer = doc.RootElement.TryGetProperty("issuer", out var issuerProp) ? issuerProp.GetString() : null;

            if (string.IsNullOrEmpty(issuer))
            {
                return (false, "Metadata was fetched successfully, but no 'issuer' property was found.");
            }

            if (issuer.TrimEnd('/') != baseUrl)
            {
                return (false, $"Issuer mismatch! Provider reports issuer as '{issuer}', but settings use '{settings.Authority}'. These MUST match exactly (excluding trailing slashes).");
            }

            return (true, "OIDC Metadata verified successfully. Issuer matches.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OIDC Pre-flight check failed");
            return (false, $"OIDC Check Error: {ex.Message}");
        }
    }

    public (bool Success, string Message) TestLdap(LdapSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Server)) return (false, "LDAP Server is required.");

        try
        {
            var identifier = new LdapDirectoryIdentifier(settings.Server, settings.Port);
            using var connection = new LdapConnection(identifier);
            
            // USER REQUIREMENT: 5 second timeout
            connection.Timeout = TimeSpan.FromSeconds(5);
            connection.SessionOptions.ProtocolVersion = 3;
            
            if (settings.UseSsl)
            {
                connection.SessionOptions.SecureSocketLayer = true;
                connection.SessionOptions.VerifyServerCertificate = (conn, cert) => true;
            }

            // We attempt a connection check. 
            // Since we don't have a "BindPassword" in settings (user-driven bind), 
            // we just check if we can open the connection.
            connection.Bind(); // Anonymous bind check to verify connectivity
            
            return (true, "LDAP Server is reachable and connection established (Anonymous Bind).");
        }
        catch (LdapException ex)
        {
            return (false, $"LDAP Connection Failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"LDAP Error: {ex.Message}");
        }
    }
}
