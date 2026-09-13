using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StacksAtlas.Core.Services.Auth;

namespace StacksAtlas.API.Auth;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly ApiKeyService _apiKeyService;
    private readonly IAuthService _authService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService apiKeyService,
        IAuthService authService) 
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
        _authService = authService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Check if the Authorization header is present
        if (!Request.Headers.TryGetValue("Authorization", out var authHeaderValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedToken = authHeaderValues.FirstOrDefault();

        // 2. We only care about tokens prefixed with Bearer sa_
        // Standard JWTs will just have "Bearer eyJ..."
        if (string.IsNullOrWhiteSpace(providedToken) || !providedToken.StartsWith("Bearer sa_", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var rawKey = providedToken.Substring("Bearer ".Length).Trim();

        // 3. Validate the key via the ApiKeyService (this hashes and compares, then updates LastUsedAt)
        var apiKeyRecord = _apiKeyService.ValidateAndTrackKey(rawKey);

        if (apiKeyRecord == null || !apiKeyRecord.IsActive)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or revoked API Key."));
        }

        // 4. Retrieve the actual user to build the ClaimsPrincipal
        var user = _authService.GetUserById(apiKeyRecord.UserId);
        
        if (user == null || !user.IsActive)
        {
            return Task.FromResult(AuthenticateResult.Fail("Associated user account is deactivated or deleted."));
        }

        // 5. Build Identity and Claims representing the user
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("id", user.Id.ToString()),
            new Claim("auth_method", "api_key") // Let endpoints know this was an API key, not a JWT
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
