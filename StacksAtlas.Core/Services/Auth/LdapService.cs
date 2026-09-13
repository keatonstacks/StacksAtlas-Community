using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Auth;

public class LdapService(SystemSettingsStore settings, ILogger<LdapService> logger)
{
    private readonly SystemSettingsStore _settings = settings;
    private readonly ILogger<LdapService> _logger = logger;

    public bool ValidateUser(string username, string password, out string? email, out List<string>? groups)
    {
        email = null;
        groups = null;

        var ldap = _settings.Current.Auth.Ldap;
        if (string.IsNullOrWhiteSpace(ldap.Server)) return false;

        try
        {
            var identifier = new LdapDirectoryIdentifier(ldap.Server, ldap.Port);
            using var connection = new LdapConnection(identifier);
            connection.Timeout = TimeSpan.FromSeconds(5);
            
            if (ldap.UseSsl)
            {
                connection.SessionOptions.SecureSocketLayer = true;
                connection.SessionOptions.VerifyServerCertificate = (conn, cert) => true; // Appliance-friendly: allow self-signed
            }

            connection.AuthType = AuthType.Basic;
            
            // Format username if filter is just a DN template or similar
            // Traditional AD: domain\user or user@domain.com
            // This implementation assumes the user provides the full identifier or we use the filter to find them.
            
            connection.Bind(new NetworkCredential(username, password));
            
            // If we reached here, bind was successful. Now fetch details.
            FetchUserDetails(connection, username, out email, out groups);
            
            return true;
        }
        catch (LdapException ex)
        {
            _logger.LogWarning("LDAP Bind failed for user {Username}: {Message}", username, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during LDAP authentication");
            return false;
        }
    }

    private void FetchUserDetails(LdapConnection connection, string username, out string? email, out List<string>? groups)
    {
        email = null;
        groups = new List<string>();

        var ldap = _settings.Current.Auth.Ldap;
        if (string.IsNullOrWhiteSpace(ldap.BaseDn)) return;

        try
        {
            var filter = string.Format(ldap.UserFilter, username);
            var searchRequest = new SearchRequest(
                ldap.BaseDn,
                filter,
                SearchScope.Subtree,
                "mail", ldap.GroupAttribute
            );

            var response = (SearchResponse)connection.SendRequest(searchRequest);
            if (response.Entries.Count > 0)
            {
                var entry = response.Entries[0];
                
                if (entry.Attributes.Contains("mail"))
                {
                    email = entry.Attributes["mail"][0]?.ToString();
                }

                if (entry.Attributes.Contains(ldap.GroupAttribute))
                {
                    foreach (var group in entry.Attributes[ldap.GroupAttribute])
                    {
                        var groupDn = group?.ToString();
                        if (groupDn != null)
                        {
                            // Extract CN from DN (e.g., CN=Admins,OU=Groups,DC=...)
                            var match = System.Text.RegularExpressions.Regex.Match(groupDn, @"CN=([^,]+)");
                            if (match.Success)
                            {
                                groups.Add(match.Groups[1].Value);
                            }
                            else
                            {
                                groups.Add(groupDn);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to fetch LDAP user details for {Username}: {Message}", username, ex.Message);
        }
    }
}
