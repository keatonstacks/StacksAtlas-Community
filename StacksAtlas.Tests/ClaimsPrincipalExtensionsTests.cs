using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using StacksAtlas.API.Extensions;

namespace StacksAtlas.Tests;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetActorUsername_ResolvesNameIdentifier_FromMappedJwtSub()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin"),
            new Claim(ClaimTypes.Role, "Admin"),
        }, "Bearer"));

        Assert.Equal("admin", principal.GetActorUsername());
    }

    [Fact]
    public void GetActorUsername_FallsBackToSubClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "operator"),
        }, "Bearer"));

        Assert.Equal("operator", principal.GetActorUsername());
    }
}
