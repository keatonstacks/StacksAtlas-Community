using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Auth;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Abstractions;

namespace StacksAtlas.API.Controllers;

[ApiController]
[Route("api/onboarding")]
public class OnboardingController(
    SystemSettingsStore settingsStore,
    FederationSettingsStore federationSettingsStore,
    NetworkSettingsStore networkSettingsStore,
    ILicenseService licenseService,
    IAuthService authService,
    INetworkService networkService,
    SystemStateProvider systemState,
    ILogger<OnboardingController> logger) : ControllerBase
{
    private readonly SystemSettingsStore _settingsStore = settingsStore;
    private readonly FederationSettingsStore _federationSettingsStore = federationSettingsStore;
    private readonly NetworkSettingsStore _networkSettingsStore = networkSettingsStore;
    private readonly ILicenseService _licenseService = licenseService;
    private readonly IAuthService _authService = authService;
    private readonly INetworkService _networkService = networkService;
    private readonly SystemStateProvider _systemState = systemState;
    private readonly ILogger<OnboardingController> _logger = logger;

    /// <summary>Bootstrap progress for the license-first wizard (§7.7).</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatus()
    {
        if (!_systemState.IsDatabaseReady)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "System initializing. Please retry shortly." });

        OnboardingLegacyMigration.ApplyIfNeeded(_settingsStore, _authService, _logger);

        var license = await _licenseService.GetCurrentStatusAsync();
        var hasUsers = _authService.AnyUsers();
        OnboardingSettingsReset.ReconcileIfNoUsers(_settingsStore, hasUsers, _logger, databaseReady: true);
        var settings = _settingsStore.Load();
        var onboarding = settings.Onboarding;
        var isFoundationComplete = OnboardingSettingsReset.GetEffectiveFoundationComplete(onboarding, hasUsers);
        var federation = _federationSettingsStore.Current;
        var isEnrolledNode = !string.IsNullOrWhiteSpace(federation.HubUrl) ||
            (federation.UseTailscaleForHubConnection &&
             (!string.IsNullOrWhiteSpace(federation.HubTailscaleMagicDns) ||
              !string.IsNullOrWhiteSpace(federation.HubTailscaleIpv4)));

        return Ok(new OnboardingStatusResponse(
            IsAdminConfigured: hasUsers,
            IsFoundationComplete: isFoundationComplete,
            ExpressBootstrapPending: onboarding.ExpressBootstrapPending && !isFoundationComplete,
            OnboardingNetworkConfirmed: onboarding.OnboardingNetworkConfirmed,
            SiteName: onboarding.SiteName,
            LegacyMigrationApplied: onboarding.LegacyMigrationApplied,
            LicenseActive: license.IsActive,
            LicenseTier: license.Tier.ToString(),
            AllowsHub: license.AllowsHub,
            AllowsFederationJoin: license.AllowsFederationJoin,
            MaxStandaloneActivations: license.MaxStandaloneActivations,
            NodeLimit: license.NodeLimit,
            HardwareId: license.HardwareId,
            LicenseMessage: license.Message,
            IsEnrolledNode: isEnrolledNode,
            PendingSiteResetRecovery: onboarding.PendingSiteResetRecovery,
            IsPortable: ExecutionState.IsPortable
        ));
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update([FromBody] OnboardingUpdateRequest request)
    {
        var license = await _licenseService.GetCurrentStatusAsync();
        if (!license.IsActive && !ExecutionState.IsPortable)
            return BadRequest(new { message = "An active license is required before completing onboarding." });

        if (request.HubOnlySetup == true && ExecutionState.IsPortable)
        {
            return BadRequest(new
            {
                message = "Fleet Hub requires a production (background service) install. Portable evaluation supports Standalone sites only."
            });
        }

        if (request.HubOnlySetup == true && !license.AllowsHub)
        {
            return BadRequest(new
            {
                message = "Central Hub mode requires a Pro, Business, or Enterprise license."
            });
        }

        var settings = _settingsStore.Load();
        var onboarding = settings.Onboarding;

        if (!string.IsNullOrWhiteSpace(request.SiteName))
            onboarding.SiteName = request.SiteName.Trim();

        if (request.ConfirmNetwork == true)
            onboarding.OnboardingNetworkConfirmed = true;

        if (request.CompleteFoundation == true)
        {
            var siteResetRecovery = onboarding.PendingSiteResetRecovery;
            var isEnrolledRecovery = siteResetRecovery?.HubInitiated == true &&
                (!string.IsNullOrWhiteSpace(_federationSettingsStore.Current.HubUrl) ||
                 _federationSettingsStore.Current.UseTailscaleForHubConnection);

            if (string.IsNullOrWhiteSpace(onboarding.SiteName))
            {
                if (ExecutionState.IsPortable)
                {
                    onboarding.SiteName = PortableOnboardingBootstrap.DefaultSiteName;
                }
                else
                {
                    var fed = _federationSettingsStore.Current;
                    if (!string.IsNullOrWhiteSpace(fed.NodeDisplayName))
                        onboarding.SiteName = fed.NodeDisplayName.Trim();
                    else if (!string.IsNullOrWhiteSpace(fed.NodeId))
                        onboarding.SiteName = fed.NodeId;
                }
            }

            if (string.IsNullOrWhiteSpace(onboarding.SiteName))
                return BadRequest(new { message = "Site name is required before completing onboarding." });

            var networkConfigured = (_networkSettingsStore.Current.Subnets?.Count ?? 0) > 0;
            var maySkipNetwork = isEnrolledRecovery && (request.SkipNetworkConfirm == true || networkConfigured);

            if (!onboarding.OnboardingNetworkConfirmed && request.HubOnlySetup != true && !maySkipNetwork)
                return BadRequest(new { message = "Confirm at least one network scope before completing onboarding." });

            onboarding.IsFoundationComplete = true;
            onboarding.ExpressBootstrapPending = false;
            onboarding.OnboardingNetworkConfirmed = onboarding.OnboardingNetworkConfirmed || maySkipNetwork;

            if (siteResetRecovery != null)
            {
                SiteResetGovernance.ClearRecoveryContext(_settingsStore, _logger);
                onboarding.PendingSiteResetRecovery = null;
            }
        }

        settings.Onboarding = onboarding;
        _settingsStore.Save(settings);

        if (!string.IsNullOrWhiteSpace(onboarding.SiteName))
        {
            SiteIdentityBootstrap.TryApplySiteName(
                _federationSettingsStore,
                onboarding.SiteName,
                _logger);
        }

        if (request.CompleteFoundation == true)
        {
            NetworkDiscoveryScopeBootstrap.EnsureAtLeastOneSubnet(
                _networkSettingsStore,
                _networkService,
                _logger);
        }

        if (request.HubOnlySetup == true && license.AllowsHub)
        {
            var federation = _federationSettingsStore.Current;
            federation.Mode = ExecutionMode.Hub;
            HubModeGuard.EnforceHubMode(federation);
            _federationSettingsStore.Save(federation);
        }

        return Ok(new
        {
            message = onboarding.IsFoundationComplete
                ? "Onboarding complete. Discovery may begin."
                : "Onboarding progress saved.",
            onboarding
        });
    }
}

public record OnboardingStatusResponse(
    bool IsAdminConfigured,
    bool IsFoundationComplete,
    bool ExpressBootstrapPending,
    bool OnboardingNetworkConfirmed,
    string SiteName,
    bool LegacyMigrationApplied,
    bool LicenseActive,
    string LicenseTier,
    bool AllowsHub,
    bool AllowsFederationJoin,
    int MaxStandaloneActivations,
    int NodeLimit,
    string HardwareId,
    string? LicenseMessage,
    bool IsEnrolledNode,
    SiteResetRecoveryContext? PendingSiteResetRecovery,
    bool IsPortable
);

public record OnboardingUpdateRequest(
    string? SiteName = null,
    bool? ConfirmNetwork = null,
    bool? CompleteFoundation = null,
    bool? HubOnlySetup = null,
    bool? SkipNetworkConfirm = null
);
