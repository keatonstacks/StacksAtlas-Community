using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Services.Network;
using StacksAtlas.API.Extensions;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Audit;
using StacksAtlas.Core.State;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DevicesController(
    IDeviceRepository repo, 
    IHostPinger pinger, 
    ILicenseService licenseService, 
    IWakeOnLanService wolService, 
    ILogger<DevicesController> logger,
    IDeviceAssetImportService assetImportService,
    IAuditService audit,
    RemoteExecutionService? fedService = null,
    IFederatedLifecyclePushService? lifecyclePush = null) : ControllerBase
{
    private readonly IDeviceRepository _repo = repo;
    private readonly IHostPinger _pinger = pinger;
    private readonly ILicenseService _licenseService = licenseService;
    private readonly IWakeOnLanService _wolService = wolService;
    private readonly ILogger<DevicesController> _logger = logger;
    private readonly IDeviceAssetImportService _assetImportService = assetImportService;
    private readonly IAuditService _audit = audit;
    private readonly RemoteExecutionService? _fedService = fedService;
    private readonly IFederatedLifecyclePushService? _lifecyclePush = lifecyclePush;

    /// <summary>
    /// When the owning Node is online, refuse Hub-only lifecycle changes that the Node rejected.
    /// </summary>
    private IActionResult? GuardFederatedRelayFailure(FederationCommandResult? relay, string actionLabel)
    {
        if (relay is not { Success: false }) return null;

        if (string.Equals(relay.Message, "Node offline", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Hub applying {Action} locally only  -  owning node is offline.",
                actionLabel);
            return null;
        }

        return UnprocessableEntity(new
        {
            message = $"{actionLabel} failed on the owning node: {relay.Message ?? "unknown error"}"
        });
    }

    private void PushLocalLifecycleToHub(Guid deviceId)
    {
        _lifecyclePush?.PushDeviceState(deviceId);
    }

    [Authorize(Roles = "Admin,Standard,Viewer")]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        int skip = 0, 
        int take = 50, 
        string? search = null, 
        string? status = null,
        string? nodeId = null,
        string? client = null,
        string? building = null,
        string? room = null,
        [FromQuery(Name = "vlanTag")] string[]? vlanTags = null,
        bool? showArchived = false,
        string? attachmentKind = null,
        string? attachmentPort = null,
        string? attachmentParentId = null)
    {
        // 1. Get paged data from repo
        var pagedDevices = _repo.GetPaged(
            skip, 
            take, 
            search, 
            status, 
            showArchived == true, 
            nodeId,
            client,
            building,
            room,
            vlanTags,
            out int totalCount,
            attachmentKind,
            attachmentPort,
            attachmentParentId);

        EnrichAttachmentParentNames(pagedDevices);
        
        var licenseStatus = await _licenseService.GetCurrentStatusAsync();

        return Ok(new {
            devices = pagedDevices,
            totalCount,
            license = new {
                licenseStatus.Tier,
                DeviceLimit = int.MaxValue,
                licenseStatus.IsActive,
                CurrentCount = _repo.GetCount(),
                IsLimited = false
            }
        });
    }

    [Authorize(Roles = "Admin,Standard,Viewer")]
    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();
        EnrichAttachmentParentNames([device]);
        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard,Viewer")]
    [HttpGet("attachment-parents")]
    public IActionResult GetAttachmentParents(
        [FromQuery] string? nodeId = null,
        [FromQuery] string? excludeDeviceId = null,
        [FromQuery] int take = 2000)
    {
        Guid? exclude = Guid.TryParse(excludeDeviceId, out var guid) ? guid : null;
        var parents = _repo.GetAttachmentParentCandidates(nodeId, exclude, take);
        var payload = parents.Select(d => new
        {
            id = d.Id,
            label = DeviceAttachmentParentCatalog.DisplayLabel(d),
            ipAddress = d.IpAddress,
            type = d.Type,
            nodeId = d.NodeId,
        });
        return Ok(payload);
    }

    private void EnrichAttachmentParentNames(List<Device> devices)
    {
        static string? DisplayName(Device parent)
        {
            if (!string.IsNullOrWhiteSpace(parent.Name))
                return parent.Name.Trim();
            if (!string.IsNullOrWhiteSpace(parent.Hostname))
                return parent.Hostname.Trim();
            return string.IsNullOrWhiteSpace(parent.IpAddress) ? null : parent.IpAddress;
        }

        foreach (var device in devices)
        {
            Device? parent = null;
            if (device.AttachmentParentDeviceId is Guid pid)
                parent = _repo.GetById(pid);
            if (parent == null && !string.IsNullOrWhiteSpace(device.AttachmentParentMac))
                parent = ResolveAttachmentParentByMac(device.AttachmentParentMac!, device.NodeId);

            if (parent == null && !string.IsNullOrWhiteSpace(device.AttachmentParentIp))
            {
                var ip = device.AttachmentParentIp.Trim();
                parent = !string.IsNullOrEmpty(device.NodeId)
                    ? _repo.GetAll().FirstOrDefault(d =>
                        !d.IsDeleted && !d.IsPermanentlyRemoved
                        && string.Equals(d.NodeId, device.NodeId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(d.IpAddress, ip, StringComparison.OrdinalIgnoreCase))
                    : _repo.GetByIp(ip);
            }

            if (parent != null)
            {
                // Normalize response to Hub-local id + name (never expose foreign Node GUIDs).
                device.AttachmentParentDeviceId = parent.Id;
                device.AttachmentParentMac = DeviceMacNormalizer.Normalize(parent.MacAddress);
                if (string.IsNullOrEmpty(device.AttachmentParentMac))
                    device.AttachmentParentMac = null;
                device.AttachmentParentIp = string.IsNullOrWhiteSpace(parent.IpAddress) ? null : parent.IpAddress.Trim();
                device.AttachmentParentName = DisplayName(parent);
            }
            else if (!string.IsNullOrWhiteSpace(device.AttachmentParentIp)
                     || !string.IsNullOrWhiteSpace(device.AttachmentParentMac)
                     || device.AttachmentParentDeviceId is Guid)
            {
                // Keep operator uplink when remap is pending (manual rows, mirror fleets).
                var hint = device.AttachmentParentIp?.Trim()
                    ?? device.AttachmentParentMac?.Trim()
                    ?? device.AttachmentParentDeviceId?.ToString();
                device.AttachmentParentName = hint;
            }
            else
            {
                device.AttachmentParentDeviceId = null;
                device.AttachmentParentName = null;
            }
        }
    }

    private Device? ResolveAttachmentParentByMac(string mac, string? nodeId)
    {
        var normalized = DeviceMacNormalizer.Normalize(mac);
        if (string.IsNullOrEmpty(normalized))
            return null;

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            return _repo.GetAll().FirstOrDefault(d =>
                !d.IsDeleted
                && !d.IsPermanentlyRemoved
                && string.Equals(d.NodeId, nodeId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.MacAddress, normalized, StringComparison.OrdinalIgnoreCase));
        }

        return _repo.GetByMac(mac);
    }

    [Authorize(Roles = "Admin,Standard,Viewer")]
    [HttpGet("vlan-tags")]
    public IActionResult GetDiscoveryVlanTags([FromQuery] string? nodeId = null)
    {
        return Ok(_repo.GetDistinctDiscoveryVlanTags(nodeId));
    }

    [Authorize(Roles = "Admin,Standard,Viewer")]
    [HttpGet("archived")]
    public IActionResult GetArchived() => Ok(_repo.GetDeleted());

    // POST: api/devices/{id}/ping
    [Authorize(Roles = "Admin,Standard")]
    [HttpPost("{id}/ping")]
    public async Task<IActionResult> RunOnDemandPing(string id)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound("Device not found in inventory");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _pinger.PingAsync(device.IpAddress, cts.Token, onDemand: true);

            device.Status = result.IsOnline ? "online" : "offline";
            if (result.IsOnline)
            {
                device.LastSeen = DateTime.Now;
                device.OpenPorts = result.OpenPorts;
                if (!string.IsNullOrEmpty(result.Hostname)) device.Hostname = result.Hostname;
            }

            _repo.UpsertDevice(device, true); // Force immediate write to DB

            return Ok(new
            {
                success = result.IsOnline,
                latency = result.RoundtripTimeMs,
                status = result.IsOnline ? "Online" : "Offline",
                details = result
            });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, "Scan timed out.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal Scan Error: {ex.Message}");
        }
    }

    // POST: api/devices/{id}/wake
    [Authorize(Roles = "Admin,Standard")]
    [HttpPost("{id}/wake")]
    public async Task<IActionResult> WakeDevice(string id)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound("Device not found");
        if (string.IsNullOrWhiteSpace(device.MacAddress)) return BadRequest("Device has no known MAC address");

        try
        {
            await _wolService.SendMagicPacketAsync(device.MacAddress);
            return Ok(new { success = true, message = $"Magic Packet sent to {device.MacAddress}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to send WOL packet: {ex.Message}");
        }
    }

    // DELETE: api/devices/{id}?hard=true
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] bool hard = false)
    {
        // Guard against the "[object Object]" ghost
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null)
            return NotFound("Device not found or could not be removed.");

        FederationCommandResult? relay = null;
        if (_fedService != null && !string.IsNullOrEmpty(device.NodeId))
        {
            relay = hard
                ? await _fedService.RelayRemoveFromFleetAsync(device, User.GetActorUsername())
                : await _fedService.RelayArchiveDeviceAsync(device);

            if (relay is { Success: false })
            {
                _logger.LogWarning(
                    "Node relay reported failure for device {DeviceId} on node {NodeId}: {Message}",
                    guidId,
                    device.NodeId,
                    relay.Message);
            }

            var relayGuard = GuardFederatedRelayFailure(relay, hard ? "Remove from fleet" : "Archive");
            if (relayGuard != null) return relayGuard;
        }

        bool wasDeleted = hard
            ? _repo.RemoveFromFleet(guidId, User.GetActorUsername())
            : _repo.Delete(guidId);

        if (!wasDeleted)
            return NotFound("Device not found or could not be removed.");

        _audit.Record(
            hard ? AuditActions.DeviceRemoveFromFleet : AuditActions.DeviceArchive,
            "device",
            guidId.ToString(),
            AuditOutcomes.Success,
            detail: device.IpAddress);

        PushLocalLifecycleToHub(guidId);

        return NoContent(); // 204: Success, no content to return
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/name")]
    public IActionResult UpdateDeviceName(string id, [FromBody] UpdateDeviceNameRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        var newName = request.Name?.Trim();
        device.Name = string.IsNullOrWhiteSpace(newName) ? device.IpAddress : newName;

        _repo.UpsertDevice(device, true);
        
        if (_fedService != null)
        {
            _ = _fedService.PushUpdateAsync(device);
        }

        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/location")]
    public IActionResult UpdateDeviceLocation(string id, [FromBody] UpdateDeviceLocationRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        device.Location = request.Location?.Trim();
        _repo.UpsertDevice(device, true);

        if (_fedService != null)
        {
            _ = _fedService.PushUpdateAsync(device);
        }

        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/model")]
    public IActionResult UpdateDeviceModel(string id, [FromBody] UpdateDeviceModelRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        device.Model = request.Model?.Trim() ?? device.Model;
        device.IsModelManuallySet = true;
        _repo.UpsertDevice(device, true);

        if (_fedService != null)
        {
            _ = _fedService.PushUpdateAsync(device);
        }

        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/vendor")]
    public IActionResult UpdateDeviceVendor(string id, [FromBody] UpdateDeviceVendorRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        device.Vendor = request.Vendor?.Trim() ?? device.Vendor;
        device.IsVendorManuallySet = true;
        _repo.UpsertDevice(device, true);

        if (_fedService != null)
        {
            _ = _fedService.PushUpdateAsync(device);
        }

        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/hostname")]
    public IActionResult UpdateDeviceHostname(string id, [FromBody] UpdateDeviceHostnameRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        var trimmed = request.Hostname?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            device.Hostname = null;
            device.IsHostnameManuallySet = false;
        }
        else
        {
            if (trimmed.Length > 253)
                return BadRequest("Hostname must be 253 characters or fewer.");

            device.Hostname = trimmed;
            device.IsHostnameManuallySet = true;
        }

        _repo.UpsertDevice(device, true);

        if (_fedService != null)
        {
            _ = _fedService.PushUpdateAsync(device);
        }

        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/type")]
    public IActionResult UpdateDeviceType(string id, [FromBody] UpdateDeviceTypeRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        var type = request.Type?.Trim();
        if (string.IsNullOrWhiteSpace(type))
            return BadRequest("Device type is required.");

        if (type.Length > 64)
            return BadRequest("Device type must be 64 characters or fewer.");

        device.Type = type;
        device.IsTypeManuallySet = true;
        device.IdentitySource = IdentitySource.Manual;
        device.ConfidenceScore = 100;
        _repo.UpsertDevice(device, true);

        if (_fedService != null)
        {
            _ = _fedService.PushUpdateAsync(device);
        }

        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/asset")]
    public IActionResult UpdateDeviceAsset(string id, [FromBody] UpdateDeviceAssetRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        if (request.SerialNumber == null
            && request.AssetTag == null
            && request.FirmwareVersion == null
            && request.WarrantyExpiresUtc == null)
        {
            return BadRequest("At least one asset field is required.");
        }

        var patchError = DeviceAssetMetadataPatcher.TryApply(
            device,
            request.SerialNumber,
            request.AssetTag,
            request.FirmwareVersion,
            request.WarrantyExpiresUtc);
        if (patchError != null)
            return BadRequest(patchError);

        _repo.UpsertDevice(device, true);

        if (_fedService != null)
            _ = _fedService.PushUpdateAsync(device);

        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/attachment")]
    public IActionResult UpdateDeviceAttachment(string id, [FromBody] UpdateDeviceAttachmentRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        var kind = NormalizeAttachmentKind(request.Kind);
        var port = string.IsNullOrWhiteSpace(request.Port) ? null : request.Port.Trim();
        if (port is { Length: > 64 })
            return BadRequest("Port / SSID must be 64 characters or fewer.");

        Guid? parentId = request.ParentDeviceId;
        if (parentId == guidId)
            return BadRequest("A device cannot be its own attachment parent.");

        string? parentMac = null;
        string? parentIp = null;
        if (parentId.HasValue)
        {
            var parent = _repo.GetById(parentId.Value);
            if (parent == null || parent.IsDeleted || parent.IsPermanentlyRemoved)
                return BadRequest("Attachment parent device was not found in inventory.");

            if (ExecutionState.IsHubBrainEnabled)
            {
                if (string.IsNullOrWhiteSpace(device.NodeId)
                    && !string.IsNullOrWhiteSpace(parent.NodeId))
                {
                    device.NodeId = parent.NodeId;
                }

                if (!string.IsNullOrEmpty(device.NodeId)
                    && !string.IsNullOrEmpty(parent.NodeId)
                    && !string.Equals(device.NodeId, parent.NodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Attachment uplink must be on the same site as the device.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(parent.NodeId))
            {
                // Standalone Node: one site per appliance; align stale rows missing nodeId.
                device.NodeId = parent.NodeId;
            }

            if (WouldCreateAttachmentCycle(guidId, parentId.Value))
                return BadRequest("Attachment parent would create a cycle.");

            parentMac = DeviceMacNormalizer.Normalize(parent.MacAddress);
            if (string.IsNullOrEmpty(parentMac))
                parentMac = null;
            parentIp = string.IsNullOrWhiteSpace(parent.IpAddress) ? null : parent.IpAddress.Trim();
        }

        device.AttachmentKind = kind;
        device.AttachmentPort = port;
        device.AttachmentParentDeviceId = parentId;
        device.AttachmentParentMac = parentMac;
        device.AttachmentParentIp = parentIp;
        device.IsAttachmentManuallySet = true;
        _repo.UpsertDevice(device, true);

        // Re-read so parent remap + display name match what the grid will show.
        device = _repo.GetById(guidId) ?? device;
        EnrichAttachmentParentNames([device]);

        if (_fedService != null)
            _ = _fedService.PushUpdateAsync(device);

        _audit.Record(
            AuditActions.DeviceAttachmentUpdate,
            "device",
            guidId.ToString(),
            AuditOutcomes.Success,
            detail: BuildAttachmentAuditDetail(device, kind, port));

        return Ok(device);
    }

    private static string BuildAttachmentAuditDetail(Device device, string kind, string? port)
    {
        static string DeviceLabel(Device d)
        {
            if (!string.IsNullOrWhiteSpace(d.Name))
                return d.Name.Trim();
            if (!string.IsNullOrWhiteSpace(d.Hostname))
                return d.Hostname.Trim();
            if (!string.IsNullOrWhiteSpace(d.IpAddress))
                return d.IpAddress.Trim();
            return d.Id.ToString();
        }

        var parts = new List<string>
        {
            $"target={DeviceLabel(device)}",
            $"kind={kind}",
        };

        if (!string.IsNullOrWhiteSpace(port))
            parts.Add($"port={port.Trim()}");

        var uplinkName = device.AttachmentParentName?.Trim();
        var uplinkIp = device.AttachmentParentIp?.Trim();
        if (!string.IsNullOrEmpty(uplinkName) || !string.IsNullOrEmpty(uplinkIp))
        {
            if (!string.IsNullOrEmpty(uplinkName) && !string.IsNullOrEmpty(uplinkIp)
                && !string.Equals(uplinkName, uplinkIp, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"uplink={uplinkName} ({uplinkIp})");
            }
            else if (!string.IsNullOrEmpty(uplinkName))
            {
                parts.Add($"uplink={uplinkName}");
            }
            else
            {
                parts.Add($"uplink={uplinkIp}");
            }
        }
        else if (device.AttachmentParentDeviceId is Guid parentId)
        {
            parts.Add($"uplink={parentId}");
        }

        if (!string.IsNullOrWhiteSpace(device.IpAddress))
            parts.Add($"ip={device.IpAddress.Trim()}");

        return string.Join("; ", parts);
    }

    private static string NormalizeAttachmentKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return "Unknown";
        return kind.Trim().ToLowerInvariant() switch
        {
            "ethernet" => "Ethernet",
            "wifi" or "wi-fi" or "wireless" => "WiFi",
            "fiber" or "fibre" or "sfp" => "Fiber",
            "other" => "Other",
            "unknown" => "Unknown",
            _ => "Unknown"
        };
    }

    private bool WouldCreateAttachmentCycle(Guid deviceId, Guid parentId)
    {
        var guard = 0;
        var current = parentId;
        while (guard++ < 64)
        {
            if (current == deviceId)
                return true;
            var parent = _repo.GetById(current);
            if (parent?.AttachmentParentDeviceId is not Guid next)
                return false;
            current = next;
        }
        return true;
    }

    /// <summary>Clears manually entered serial/firmware only (preserves OpenAVC-enriched values).</summary>
    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/asset/clear-manual")]
    public IActionResult ClearManualDeviceAssetFields(string id)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        if (!DeviceAssetMetadataPatcher.TryClearManualSerialAndFirmware(device))
            return Ok(device);

        _repo.UpsertDevice(device, true);

        if (_fedService != null)
            _ = _fedService.PushUpdateAsync(device);

        return Ok(device);
    }

    /// <summary>Bulk-update asset fields from CSV. Match order: Device ID, then MAC, then IP (optional Node ID on Hub).</summary>
    [Authorize(Roles = "Admin,Standard")]
    [HttpPost("asset-import")]
    public IActionResult ImportDeviceAssets([FromBody] DeviceAssetImportRequest request)
    {
        if (request.Rows is { Count: > 0 })
        {
            var assetRows = request.Rows.Select((r, i) => new AssetCsvRow
            {
                LineNumber = i + 2,
                DeviceId = r.DeviceId,
                NodeId = r.NodeId,
                MacAddress = r.MacAddress,
                IpAddress = r.IpAddress,
                SerialNumber = r.SerialNumber,
                AssetTag = r.AssetTag,
                FirmwareVersion = r.FirmwareVersion,
                WarrantyExpires = r.WarrantyExpires,
                HasSerialNumber = r.SerialNumber != null,
                HasAssetTag = r.AssetTag != null,
                HasFirmwareVersion = r.FirmwareVersion != null,
                HasWarrantyExpires = r.WarrantyExpires != null,
            }).ToList();

            var result = _assetImportService.ImportRows(assetRows);
            PushFederationAssetUpdates(result);
            return Ok(result);
        }

        if (string.IsNullOrWhiteSpace(request.Csv))
            return BadRequest("Provide csv text or rows.");

        var csvResult = _assetImportService.ImportCsv(request.Csv);
        PushFederationAssetUpdates(csvResult);
        return Ok(csvResult);
    }

    private void PushFederationAssetUpdates(DeviceAssetImportResult result)
    {
        if (_fedService == null || result.UpdatedDeviceIds.Count == 0) return;

        foreach (var deviceId in result.UpdatedDeviceIds)
        {
            var device = _repo.GetById(deviceId);
            if (device != null)
                _ = _fedService.PushUpdateAsync(device);
        }
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/manager")]
    public IActionResult UpdateDeviceManager(string id, [FromBody] UpdateDeviceManagerRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        device.ManagedByUserId = request.UserId;
        device.ManagedByUsername = request.Username;

        _repo.UpsertDevice(device, true);

        if (_fedService != null)
            _ = _fedService.PushUpdateAsync(device);

        return Ok(device);
    }

    [Authorize(Roles = "Admin,Standard")]
    [HttpPatch("{id}/alerts")]
    public IActionResult UpdateDeviceAlerts(string id, [FromBody] UpdateDeviceAlertsRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        device.AlertsEnabled = request.Enabled;

        _repo.UpsertDevice(device, true);

        if (_fedService != null)
            _ = _fedService.PushUpdateAsync(device);

        return Ok(device);
    }
    // POST: api/devices/{id}/restore
    [Authorize(Roles = "Admin,Standard")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null) return NotFound();

        if (device.IsPermanentlyRemoved)
            return BadRequest(new { message = "Device is permanently removed. Use restore-to-fleet instead." });

        FederationCommandResult? relay = null;
        if (_fedService != null && !string.IsNullOrEmpty(device.NodeId))
        {
            relay = await _fedService.RelayRestoreDeviceAsync(device);
            if (relay is { Success: false })
            {
                _logger.LogWarning(
                    "Node relay reported failure for restore on device {DeviceId} node {NodeId}: {Message}",
                    guidId,
                    device.NodeId,
                    relay.Message);
            }

            var relayGuard = GuardFederatedRelayFailure(relay, "Restore");
            if (relayGuard != null) return relayGuard;
        }

        var success = _repo.Restore(guidId);
        if (!success) return NotFound();

        _audit.Record(AuditActions.DeviceRestore, "device", guidId.ToString(), AuditOutcomes.Success, detail: device.IpAddress);

        PushLocalLifecycleToHub(guidId);

        return Ok(new { message = "Device restored to inventory" });
    }

    /// <summary>Permanently removed devices (§7.9 Phase B). Admin-only; Phase C adds UI view.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("removed")]
    public IActionResult GetPermanentlyRemoved()
    {
        var devices = _repo.GetPermanentlyRemoved()
            .OrderByDescending(d => d.RemovedUtc ?? d.LastModifiedUtc)
            .ToList();
        return Ok(devices);
    }

    /// <summary>Re-admit a tombstoned device to fleet inventory (§7.9 Phase B).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/restore-to-fleet")]
    public async Task<IActionResult> RestoreToFleet(string id)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        var device = _repo.GetById(guidId);
        if (device == null || !device.IsPermanentlyRemoved)
            return NotFound("Permanently removed device not found");

        FederationCommandResult? relay = null;
        if (_fedService != null && !string.IsNullOrEmpty(device.NodeId))
        {
            relay = await _fedService.RelayRestoreFromFleetAsync(device);
            if (relay is { Success: false })
            {
                _logger.LogWarning(
                    "Node relay reported failure for restore-to-fleet on device {DeviceId} node {NodeId}: {Message}",
                    guidId,
                    device.NodeId,
                    relay.Message);
            }

            var relayGuard = GuardFederatedRelayFailure(relay, "Restore to fleet");
            if (relayGuard != null) return relayGuard;
        }

        if (!_repo.RestoreFromFleet(guidId))
            return NotFound("Device could not be restored to fleet");

        _audit.Record(AuditActions.DeviceRestoreToFleet, "device", guidId.ToString(), AuditOutcomes.Success, detail: device.IpAddress);

        PushLocalLifecycleToHub(guidId);

        return Ok(new { message = "Device restored to fleet inventory" });
    }

    // POST: api/devices/{id}/metrics/reset
    [Authorize(Roles = "Admin,Standard")]
    [HttpPost("{id}/metrics/reset")]
    public async Task<IActionResult> ResetMetrics(string id)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        if (_repo.ResetMetrics(guidId))
        {
            var device = _repo.GetById(guidId);
            if (device != null && !string.IsNullOrEmpty(device.NodeId) && _fedService != null)
            {
                var relay = await _fedService.RelayResetMetricsAsync(device);
                if (relay is { Success: false } &&
                    !string.Equals(relay.Message, "Node offline", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Hub reset metrics locally but node relay failed for device {DeviceId}: {Message}",
                        guidId,
                        relay.Message);
                }
            }

            return Ok(new { message = "Metrics reset successfully" });
        }
        return NotFound();
    }

    // POST: api/devices/{id}/risks/acknowledge
    [Authorize(Roles = "Admin,Standard")]
    [HttpPost("{id}/risks/acknowledge")]
    public async Task<IActionResult> AcknowledgeRisk(string id, [FromBody] AcknowledgeRiskRequest request)
    {
        if (id == "[object Object]" || !Guid.TryParse(id, out var guidId))
            return BadRequest("Invalid ID format");

        if (string.IsNullOrWhiteSpace(request.Issue))
            return BadRequest("Issue cannot be empty");

        if (!DeviceRiskAcknowledgement.TryAcknowledge(_repo, guidId, request.Issue))
            return NotFound();

        var device = _repo.GetById(guidId);
        if (device != null && !string.IsNullOrEmpty(device.NodeId) && _fedService != null)
        {
            var relay = await _fedService.RelayAcknowledgeSecurityRiskAsync(device, request.Issue);
            if (relay is { Success: false } &&
                !string.Equals(relay.Message, "Node offline", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Hub acknowledged risk locally but node relay failed for device {DeviceId}: {Message}",
                    guidId,
                    relay.Message);
            }
        }

        _audit.Record(
            AuditActions.DeviceRiskAcknowledge,
            "device",
            guidId.ToString(),
            AuditOutcomes.Success,
            detail: request.Issue.Length > 120 ? request.Issue[..120] : request.Issue);

        return Ok(new { message = "Risk acknowledged" });
    }
}

public record UpdateDeviceNameRequest(string? Name);
public record UpdateDeviceLocationRequest(string? Location);
public record UpdateDeviceModelRequest(string? Model);
public record UpdateDeviceVendorRequest(string? Vendor);
public record UpdateDeviceHostnameRequest(string? Hostname);
public record UpdateDeviceTypeRequest(string? Type);
public record UpdateDeviceAssetRequest(
    string? SerialNumber,
    string? AssetTag,
    string? FirmwareVersion,
    string? WarrantyExpiresUtc);
public record UpdateDeviceAttachmentRequest(string? Kind, string? Port, Guid? ParentDeviceId);
public record DeviceAssetImportRequest(string? Csv, List<DeviceAssetImportRowDto>? Rows);
public record DeviceAssetImportRowDto(
    string? DeviceId,
    string? NodeId,
    string? MacAddress,
    string? IpAddress,
    string? SerialNumber,
    string? AssetTag,
    string? FirmwareVersion,
    string? WarrantyExpires);
public record UpdateDeviceManagerRequest(Guid? UserId, string? Username);
public record UpdateDeviceAlertsRequest(bool Enabled);
public record AcknowledgeRiskRequest(string Issue);
