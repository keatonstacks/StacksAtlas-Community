using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using StacksAtlas.API.Auth;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Settings;
using System.Text;
using System.Security.Claims;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.API.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddStacksAtlasAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtKeyBytes = AuthService.ResolveSigningKey(configuration);

        // Register the Change Token Source so OIDC options reload when settings change
        services.AddSingleton<IOptionsChangeTokenSource<OpenIdConnectOptions>>(sp => 
            new SystemSettingsChangeTokenSource("OIDC", sp.GetRequiredService<SystemSettingsStore>()));

        var authBuilder = services.AddAuthentication(options => 
            {
                options.DefaultAuthenticateScheme = "BearerOrApiKey";
                options.DefaultChallengeScheme = "BearerOrApiKey";
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = "StacksAtlas.Sso.State";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.HttpOnly = true;
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "StacksAtlas",
                    ValidAudience = "StacksAtlasUsers",
                    IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = ClaimTypes.Role,
                };
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.DefaultScheme, null);

        authBuilder.AddOpenIdConnect("OIDC", options =>
        {
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.CallbackPath = "/api/auth/login/sso/callback";
            
            // Allow the handler to be re-initialized when options change
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IAuthService>>();
                    logger.LogInformation("SSO: Challenge Initiated. Authority: {Authority}, ClientId: {ClientId}", 
                        context.Options.Authority, context.Options.ClientId);
                    return Task.CompletedTask;
                },
                OnRemoteFailure = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IAuthService>>();
                    logger.LogError(context.Failure, "SSO: Remote Authentication Failure: {Message}", context.Failure?.Message);
                    
                    var request = context.HttpContext.Request;
                    var targetUrl = "/login?error=sso_failure";
                    var referer = request.Headers["Referer"].ToString();
                    if (!string.IsNullOrEmpty(referer))
                    {
                        var uri = new Uri(referer);
                        targetUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}/login?error=sso_failure";
                    }

                    context.Response.Redirect(targetUrl);
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IAuthService>>();
                    logger.LogInformation("SSO: Token Validated Successfully.");
                    var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                    
                    var externalId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                                    ?? context.Principal?.FindFirst("sub")?.Value;
                    
                    var username = context.Principal?.FindFirst(ClaimTypes.Name)?.Value 
                                  ?? context.Principal?.FindFirst("name")?.Value 
                                  ?? context.Principal?.FindFirst("preferred_username")?.Value;
                    
                    var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value 
                               ?? context.Principal?.FindFirst("email")?.Value;

                    if (string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(email))
                        username = email;
                    
                    if (string.IsNullOrEmpty(username)) username = "sso_user";

                    var groups = context.Principal?.FindAll("groups").Select(c => c.Value).ToList() 
                                 ?? context.Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

                    if (!string.IsNullOrEmpty(externalId))
                    {
                        var user = authService.GetOrCreateExternalUser("OIDC", externalId, username, email, groups);
                        var token = authService.GenerateToken(user);
                        
                        var targetUrl = context.Properties?.RedirectUri ?? "/";
                        // Hand off JWT via short-lived HttpOnly cookie  -  never in query string (browser history / Referer).
                        context.Response.Cookies.Append(
                            SsoHandoffCookie.Name,
                            token,
                            new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = context.Request.IsHttps,
                                SameSite = SameSiteMode.Lax,
                                MaxAge = TimeSpan.FromSeconds(SsoHandoffCookie.MaxAgeSeconds),
                                Path = "/"
                            });

                        var separator = targetUrl.Contains('?') ? "&" : "?";
                        var redirectUrl = $"{targetUrl}{separator}sso=handoff";
                        logger.LogInformation("SSO: Handshake complete. Redirecting to dashboard (cookie handoff).");
                        context.Response.Redirect(redirectUrl);
                        context.HandleResponse();
                    }
                }
            };
        });

        // Binds runtime settings to the OIDC options
        services.AddOptions<OpenIdConnectOptions>("OIDC")
            .Configure<SystemSettingsStore, SettingsEncryptor>((options, store, encrypt) =>
            {
                var sso = store.Current.Auth;
                
                // OIDC library requires these to be non-null even if the scheme is never challenged
                options.Authority = "https://localhost"; 
                options.ClientId = "DISABLED";

                if (sso.SsoEnabled && sso.Provider == "OIDC")
                {
                    options.Authority = sso.Oidc.Authority ?? "https://localhost";
                    options.ClientId = sso.Oidc.ClientId ?? "DISABLED";
                    options.ClientSecret = encrypt.Unprotect(sso.Oidc.ClientSecret ?? "");
                    
                    options.Scope.Clear();
                    if (!string.IsNullOrEmpty(sso.Oidc.Scope))
                    {
                        foreach (var scope in sso.Oidc.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            options.Scope.Add(scope);
                        }
                    }

                    // Force Azure AD compatibility if authority contains microsoftonline.com
                    if (options.Authority.Contains("microsoftonline.com"))
                    {
                        options.TokenValidationParameters.ValidateIssuer = false;
                    }
                }
            });

        authBuilder.AddPolicyScheme("BearerOrApiKey", "Bearer or API Key", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                if (authHeader != null && authHeader.StartsWith("Bearer sa_"))
                {
                    return ApiKeyAuthenticationOptions.DefaultScheme;
                }
                return JwtBearerDefaults.AuthenticationScheme;
            };
        });

        return services;
    }
}

/// <summary>
/// Triggers a reload of OIDC options when the SystemSettingsStore fires its change event.
/// </summary>
public class SystemSettingsChangeTokenSource : IOptionsChangeTokenSource<OpenIdConnectOptions>
{
    private readonly SystemSettingsStore _store;
    private ConfigurationReloadToken _token = new();

    public string Name { get; }

    public SystemSettingsChangeTokenSource(string name, SystemSettingsStore store)
    {
        Name = name;
        _store = store;
        _store.OnSettingsChanged += () => 
        {
            var oldToken = Interlocked.Exchange(ref _token, new ConfigurationReloadToken());
            oldToken.OnReload();
        };
    }

    public IChangeToken GetChangeToken() => _token;
}
