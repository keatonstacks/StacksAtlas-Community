using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using StacksAtlas.API.Auth;
using StacksAtlas.API.Models;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Audit;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _config;
    private readonly SystemSettingsStore _settingsStore;
    private readonly SystemStateProvider _systemState;
    private readonly IAuditService _audit;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IConfiguration config,
        SystemSettingsStore settingsStore,
        SystemStateProvider systemState,
        IAuditService audit,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _config = config;
        _settingsStore = settingsStore;
        _systemState = systemState;
        _audit = audit;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("status")]
    public ActionResult<AuthStatusResponse> GetStatus()
    {
        if (!_systemState.IsDatabaseReady)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "System initializing. Please retry shortly." });

        return Ok(new AuthStatusResponse(_authService.AnyUsers()));
    }

    [AllowAnonymous]
    [HttpPost("setup")]
    public IActionResult Setup([FromBody] SetupRequest request)
    {
        if (_authService.AnyUsers())
        {
            return BadRequest("Setup already completed. Please login.");
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Username and Password are required.");
        }

        var user = _authService.RegisterAdmin(request.Username, request.Password);
        if (user == null) return StatusCode(500, "Failed to create user");

        OnboardingSettingsReset.BeginExpressBootstrap(_settingsStore, _logger);

        _audit.Record(
            AuditActions.AuthSetup,
            "user",
            user.Id.ToString(),
            AuditOutcomes.Success,
            actorUsernameOverride: user.Username,
            actorUserIdOverride: user.Id.ToString(),
            actorRoleOverride: user.Role);

        // Auto-login after setup
        var token = _authService.GenerateToken(user);
        return Ok(new { Token = token, Username = user.Username });
    }

    /// <summary>Issues a JWT for the internal evaluation account  -  portable mode only.</summary>
    [AllowAnonymous]
    [HttpPost("portable-session")]
    public IActionResult PortableSession()
    {
        if (!ExecutionState.IsPortable)
            return NotFound(new { message = "Portable session is only available in evaluation mode." });

        if (!_authService.AnyUsers())
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "System initializing. Please retry shortly." });
        }

        var user = _authService.GetAllUsers()
            .FirstOrDefault(u => u.IsActive && string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase));

        if (user == null)
            return StatusCode(500, new { message = "Evaluation account is not ready." });

        var token = _authService.GenerateToken(user);
        return Ok(new { Token = token, Username = user.Username });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Invalid credentials.");
        }

        var user = _authService.ValidateUser(request.Username, request.Password);
        if (user == null)
        {
            _audit.Record(
                AuditActions.AuthLoginFailed,
                "auth",
                request.Username,
                AuditOutcomes.Failed,
                detail: "Invalid username or password",
                actorUsernameOverride: request.Username);
            return Unauthorized("Invalid username or password.");
        }

        _audit.Record(
            AuditActions.AuthLoginSuccess,
            "user",
            user.Id.ToString(),
            AuditOutcomes.Success,
            actorUsernameOverride: user.Username,
            actorUserIdOverride: user.Id.ToString(),
            actorRoleOverride: user.Role);

        var token = _authService.GenerateToken(user);
        return Ok(new { Token = token, Username = user.Username });
    }

    [Authorize]
    [HttpPost("refresh")]
    public IActionResult Refresh()
    {
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader == null || !authHeader.StartsWith("Bearer "))
        {
            return Unauthorized();
        }

        var oldToken = authHeader.Substring(7);
        var principal = _authService.GetPrincipalFromToken(oldToken);
        
        if (principal == null)
        {
            return Unauthorized("Invalid token.");
        }

        var userIdStr = principal.FindFirst("id")?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized("Invalid user ID in token.");
        }

        var user = _authService.GetUserById(userId);
        if (user == null || !user.IsActive)
        {
            return Unauthorized("User no longer exists or is inactive.");
        }

        var newToken = _authService.GenerateToken(user);
        return Ok(new { Token = newToken, Username = user.Username });
    }

    [HttpGet("login/sso")]
    public IActionResult LoginSso()
    {
        var settingsStore = HttpContext.RequestServices.GetRequiredService<SystemSettingsStore>();
        if (!settingsStore.Current.Auth.SsoEnabled)
        {
            return BadRequest("SSO is not enabled.");
        }

        var referer = Request.Headers["Referer"].ToString();
        var redirectUri = "/";
        if (!string.IsNullOrEmpty(referer))
        {
            var uri = new Uri(referer);
            // We want to redirect back to the frontend root (or wherever they came from)
            redirectUri = $"{uri.Scheme}://{uri.Host}:{uri.Port}/";
        }

        return Challenge(new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = redirectUri
        }, settingsStore.Current.Auth.Provider);
    }

    [HttpGet("sso/config")]
    public IActionResult GetSsoConfig()
    {
        var settingsStore = HttpContext.RequestServices.GetRequiredService<SystemSettingsStore>();
        return Ok(new
        {
            Enabled = settingsStore.Current.Auth.SsoEnabled,
            Provider = settingsStore.Current.Auth.Provider
        });
    }

    /// <summary>
    /// Consumes the one-time SSO handoff cookie set after OIDC callback. Returns JWT once, then clears the cookie.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("sso/complete")]
    public IActionResult CompleteSsoHandoff()
    {
        if (!Request.Cookies.TryGetValue(SsoHandoffCookie.Name, out var token) || string.IsNullOrWhiteSpace(token))
            return NoContent();

        Response.Cookies.Delete(SsoHandoffCookie.Name, new CookieOptions { Path = "/" });

        var principal = _authService.GetPrincipalFromToken(token);
        if (principal == null)
            return Unauthorized(new { message = "Invalid SSO handoff token." });

        var username = principal.FindFirst("unique_name")?.Value
                       ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                       ?? "sso_user";
        var userId = principal.FindFirst("id")?.Value
                     ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        _audit.Record(
            AuditActions.AuthSsoLogin,
            "user",
            userId,
            AuditOutcomes.Success,
            actor: principal,
            actorUsernameOverride: username,
            actorUserIdOverride: userId,
            actorRoleOverride: role);

        return Ok(new { Token = token, Username = username });
    }
}
