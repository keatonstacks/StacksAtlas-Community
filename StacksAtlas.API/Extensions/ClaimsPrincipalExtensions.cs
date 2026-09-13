using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace StacksAtlas.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Resolve the operator username from JWT (Sub) or legacy Name claim.
    /// </summary>
    public static string? GetActorUsername(this ClaimsPrincipal? user)
    {
        if (user == null) return null;

        // Local JWT maps Sub → NameIdentifier (inbound claim mapping). Check both.
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name;
    }
}
