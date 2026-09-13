using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/settings/network")]

public class NetworkSettingsController(
    NetworkSettingsStore store,
    IAuditService audit,
    ILogger<NetworkSettingsController> logger) : ControllerBase
{
    private readonly NetworkSettingsStore _store = store;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<NetworkSettingsController> _logger = logger;

    [HttpGet]
    public ActionResult<NetworkSettings> GetSettings()
    {
        var settings = _store.Load();
        return Ok(settings);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut]
    public IActionResult UpdateSettings([FromBody] NetworkSettingsUpdateDto updatedSettings)
    {
        if (updatedSettings == null)
            return BadRequest("Settings cannot be null.");

        try 
        {
            // Lock and load
            var settings = _store.Load();

            // Only update fields that are explicitly provided (not null)
            if (updatedSettings.Subnets != null) 
                settings.Subnets = updatedSettings.Subnets;

            if (updatedSettings.InterfaceConfigs != null)
                settings.InterfaceConfigs = updatedSettings.InterfaceConfigs;

            if (updatedSettings.InterfaceRoleMappings != null && updatedSettings.InterfaceConfigs == null)
                settings.InterfaceRoleMappings = updatedSettings.InterfaceRoleMappings;

            if (updatedSettings.PingTimeoutMs.HasValue && updatedSettings.PingTimeoutMs > 0)
                settings.PingTimeoutMs = updatedSettings.PingTimeoutMs.Value;

            if (updatedSettings.PingRetries.HasValue)
                settings.PingRetries = updatedSettings.PingRetries.Value;

            if (updatedSettings.MaxParallelPings.HasValue && updatedSettings.MaxParallelPings > 0)
                settings.MaxParallelPings = updatedSettings.MaxParallelPings.Value;

            if (updatedSettings.OfflineStrikeThreshold.HasValue && updatedSettings.OfflineStrikeThreshold > 0)
                settings.OfflineStrikeThreshold = updatedSettings.OfflineStrikeThreshold.Value;

            if (updatedSettings.EnableInstantRecovery.HasValue)
                settings.EnableInstantRecovery = updatedSettings.EnableInstantRecovery.Value;

            if (updatedSettings.EnableNmapDeepScan.HasValue)
                settings.EnableNmapDeepScan = updatedSettings.EnableNmapDeepScan.Value;

            if (updatedSettings.MaxConcurrentDeepScans.HasValue && updatedSettings.MaxConcurrentDeepScans > 0)
                settings.MaxConcurrentDeepScans = updatedSettings.MaxConcurrentDeepScans.Value;

            if (settings.Subnets != null)
            {
                for (var i = 0; i < settings.Subnets.Count; i++)
                {
                    var scope = settings.Subnets[i];
                    if (!VlanTagValidator.TryNormalize(scope.VlanTag, out var normalized, out var vlanError))
                        return BadRequest($"Scope {i + 1} ({scope.Cidr}): {vlanError}");

                    scope.VlanTag = normalized;
                }
            }

            if (settings.InterfaceConfigs != null)
            {
                for (var i = 0; i < settings.InterfaceConfigs.Count; i++)
                {
                    var config = settings.InterfaceConfigs[i];
                    if (!VlanTagValidator.TryNormalize(config.DefaultVlanTag, out var normalized, out var vlanError))
                        return BadRequest($"Adapter policy {i + 1}: {vlanError}");

                    config.DefaultVlanTag = normalized;
                }
            }
            
            _store.Save(settings);
            _audit.Record(
                AuditActions.SettingsNetworkUpdate,
                "settings",
                "network",
                AuditOutcomes.Success,
                detail: $"subnets={settings.Subnets?.Count ?? 0}; nmap={settings.EnableNmapDeepScan}");
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save network settings.");
            return StatusCode(500, $"Failed to save settings: {ex.Message}");
        }
    }
}

// DTO for Partial Updates
public class NetworkSettingsUpdateDto
{
    public List<NetworkScope>? Subnets { get; set; }
    public List<NetworkInterfaceConfig>? InterfaceConfigs { get; set; }
    public List<InterfaceRoleMapping>? InterfaceRoleMappings { get; set; }
    public int? PingTimeoutMs { get; set; }
    public int? PingRetries { get; set; }
    public int? MaxParallelPings { get; set; }
    public int? OfflineStrikeThreshold { get; set; }
    public bool? EnableInstantRecovery { get; set; }
    public bool? EnableNmapDeepScan { get; set; }
    public int? MaxConcurrentDeepScans { get; set; }
}


