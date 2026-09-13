using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using StacksAtlas.Core.Services.Audit;
using Xunit;

namespace StacksAtlas.Tests;

public class AuditActorResolverTests
{
    [Fact]
    public void ResolveUsername_UsesJwtSubClaim()
    {
        var actor = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "admin"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("id", "11111111-1111-1111-1111-111111111111"),
        ],
        "Bearer"));

        Assert.Equal("admin", AuditActorResolver.ResolveUsername(actor));
        Assert.Equal("Admin", AuditActorResolver.ResolveRole(actor));
        Assert.Equal("11111111-1111-1111-1111-111111111111", AuditActorResolver.ResolveUserId(actor));
    }

    [Fact]
    public void ResolveUsername_UsesNameIdentifierForApiKeyPrincipal()
    {
        var actor = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "api-user"),
            new Claim(ClaimTypes.Role, "Standard"),
            new Claim("id", "22222222-2222-2222-2222-222222222222"),
        ],
        "ApiKey"));

        Assert.Equal("api-user", AuditActorResolver.ResolveUsername(actor));
    }

    [Fact]
    public void ResolveUsername_ReturnsUnknownWhenMissing()
    {
        Assert.Equal("unknown", AuditActorResolver.ResolveUsername(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Equal("unknown", AuditActorResolver.ResolveUsername(null));
    }
}
