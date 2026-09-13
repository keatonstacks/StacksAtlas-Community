using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LicenseController(
    ILicenseService licenseService,
    IHardwareIdProvider hardwareIdProvider,
    IAuthService authService,
    SystemSettingsStore settingsStore,
    IAuditService audit) : ControllerBase
{
    private readonly ILicenseService _licenseService = licenseService;
    private readonly IHardwareIdProvider _hardwareIdProvider = hardwareIdProvider;
    private readonly IAuthService _authService = authService;
    private readonly SystemSettingsStore _settingsStore = settingsStore;
    private readonly IAuditService _audit = audit;

    [AllowAnonymous]
    [HttpGet("hardware-id")]
    public IActionResult GetHardwareId() =>
        Ok(new { hardwareId = _hardwareIdProvider.GetHardwareId() });

    [Authorize]
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var status = await _licenseService.GetCurrentStatusAsync();
        return Ok(status);
    }

    /// <summary>
    /// Anonymous only during first-run onboarding; requires Admin after foundation setup is complete.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivateRequest request)
    {
        if (!IsOnboardingActivationAllowed())
        {
            if (User?.Identity?.IsAuthenticated != true)
                return Unauthorized(new { message = "Admin authentication required to activate a license." });

            if (!User.IsInRole("Admin"))
                return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.LicenseKey))
            return BadRequest("License key is required");

        var status = await _licenseService.ActivateAsync(request.LicenseKey);

        _audit.Record(
            AuditActions.LicenseActivate,
            "license",
            null,
            AuditOutcomes.Success,
            detail: $"tier={status.Tier}");

        return Ok(status);
    }

    private bool IsOnboardingActivationAllowed()
    {
        if (!_authService.AnyUsers())
            return true;

        var onboarding = _settingsStore.Load().Onboarding;
        return !OnboardingSettingsReset.GetEffectiveFoundationComplete(onboarding, hasUsers: true);
    }
}

public record ActivateRequest(string LicenseKey);
