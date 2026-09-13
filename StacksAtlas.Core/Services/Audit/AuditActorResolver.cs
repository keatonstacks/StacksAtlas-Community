using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace StacksAtlas.Core.Services.Audit;

/// <summary>
/// Resolves audit actor fields from JWT, API key, or SSO principals.
/// </summary>
public static class AuditActorResolver
{
    public static string ResolveUsername(ClaimsPrincipal? actor, string? overrideUsername = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideUsername))
            return overrideUsername.Trim();

        var username = actor?.FindFirst("username")?.Value
            ?? actor?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? actor?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? actor?.Identity?.Name;

        return string.IsNullOrWhiteSpace(username) ? "unknown" : username.Trim();
    }

    public static string ResolveUserId(ClaimsPrincipal? actor, string? overrideUserId = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideUserId))
            return overrideUserId.Trim();

        return actor?.FindFirst("id")?.Value ?? string.Empty;
    }

    public static string ResolveRole(ClaimsPrincipal? actor, string? overrideRole = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRole))
            return overrideRole.Trim();

        return actor?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
    }
}
