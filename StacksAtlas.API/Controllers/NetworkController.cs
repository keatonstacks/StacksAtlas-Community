using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Network;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NetworkController(
    INetworkInterfaceService networkService,
    INetworkRoutingDiagnosticsService routingDiagnosticsService) : ControllerBase
{
    private readonly INetworkInterfaceService _networkService = networkService;
    private readonly INetworkRoutingDiagnosticsService _routingDiagnosticsService = routingDiagnosticsService;

    [HttpGet("interfaces")]
    public IActionResult GetInterfaces([FromQuery] string? scope = "discovery")
    {
        var resolvedScope = string.Equals(scope, "policy", StringComparison.OrdinalIgnoreCase)
            ? StacksAtlas.Core.Models.NetworkInterfaceScope.Policy
            : StacksAtlas.Core.Models.NetworkInterfaceScope.Discovery;
        return Ok(_networkService.GetAllInterfaces(resolvedScope));
    }

    [HttpGet("routing-diagnostics")]
    public IActionResult GetRoutingDiagnostics()
    {
        return Ok(_routingDiagnosticsService.Analyze());
    }

    [HttpGet("active")]
    public IActionResult GetActiveInterface()
    {
        return Ok(_networkService.GetActiveInterface());
    }

    [HttpGet("range")]
    public IActionResult GetActiveRange()
    {
        return Ok(new { cidr = _networkService.GetScannerCidr() });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("interface")]
    public IActionResult SetInterface([FromBody] SetInterfaceRequest request)
    {
        _networkService.SetActiveInterface(request.Id);
        return Ok(new { message = "Active interface updated." });
    }

    [HttpGet("status")]
    public IActionResult GetScanStatus([FromServices] ScanProgressService progressService)
    {
        return Ok(new 
        { 
            isScanning = progressService.IsScanning,
            phase = progressService.CurrentPhase,
            progress = progressService.ProgressPercentage 
        });
    }

    [HttpGet("ptp")]
    public async Task<IActionResult> GetPtpStatus()
    {
        var activeInterface = _networkService.GetActiveInterface();
        string? localIp = activeInterface.IpAddress != "0.0.0.0" ? activeInterface.IpAddress : null;

        // Scan for 1.5 seconds to find the master on the specific interface
        var master = await PtpListener.DetectGrandmasterAsync(localIp, 1500);
        if (master == null) return NoContent();

        return Ok(master);
    }

    /// <summary>
    /// Inventory topology: parent attachment edges when set; otherwise hang off the default gateway.
    /// Optional <paramref name="nodeId"/> scopes Hub fleet maps to one site.
    /// </summary>
    [HttpGet("topology")]
    public IActionResult GetTopology(
        [FromServices] IDeviceRepository deviceRepo,
        [FromQuery] string? nodeId = null)
    {
        var devices = deviceRepo.GetAll();
        var scopedToSite = !string.IsNullOrWhiteSpace(nodeId);
        if (scopedToSite)
            devices = devices.Where(d => string.Equals(d.NodeId, nodeId, StringComparison.OrdinalIgnoreCase)).ToList();

        var activeInterface = _networkService.GetActiveInterface();
        var rootId = "root_gateway";

        StacksAtlas.Core.Models.Device? gatewayDevice;
        string? gatewayIp;
        if (scopedToSite)
        {
            gatewayDevice = TopologyGatewayResolver.ResolveSiteGateway(devices);
            gatewayIp = gatewayDevice?.IpAddress ?? "Unknown";
        }
        else
        {
            gatewayIp = activeInterface.GatewayAddress;
            gatewayDevice = TopologyGatewayResolver.ResolveByGatewayIp(devices, gatewayIp);
        }

        var rootName = gatewayDevice?.Name ?? "Gateway Router";
        if (string.IsNullOrWhiteSpace(rootName))
            rootName = gatewayIp ?? "Unknown";

        var deviceById = devices.ToDictionary(d => d.Id);
        // Resolve uplink targets (GUID or MAC) so child counts and links stay consistent on Hub.
        var resolvedParentByChild = devices.ToDictionary(
            d => d.Id,
            d => ResolveAttachmentParentId(d, gatewayDevice?.Id, deviceById));
        var childCountByParent = resolvedParentByChild
            .Where(kv => kv.Value is Guid)
            .GroupBy(kv => kv.Value!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var nodes = new List<object>
        {
            new
            {
                id = rootId,
                name = rootName,
                group = gatewayDevice?.Type ?? "Gateway",
                val = 20,
                status = gatewayDevice?.Status ?? "online",
                ip = gatewayIp ?? "Unknown",
                attachmentKind = (string?)null,
                attachmentPort = (string?)null,
                isInfrastructure = true,
                childCount = devices.Count(d => d.Id != gatewayDevice?.Id),
                securityGrade = gatewayDevice?.SecurityGrade.ToString() ?? "Green",
                firstDiscoveredUtc = gatewayDevice?.FirstDiscoveredUtc,
                vendor = gatewayDevice?.Vendor,
            }
        };

        var links = new List<object>();

        foreach (var d in devices)
        {
            // Gateway inventory row is represented by root_gateway.
            if (gatewayDevice != null && d.Id == gatewayDevice.Id)
                continue;

            var isInfra = IsInfrastructureType(d.Type);
            var size = ResolveNodeSize(d.Type, isInfra);
            if (childCountByParent.TryGetValue(d.Id, out var kids) && kids > 0)
                size = Math.Max(size, 12 + Math.Min(kids, 8));

            nodes.Add(new
            {
                id = d.Id.ToString(),
                name = string.IsNullOrWhiteSpace(d.Name) ? d.IpAddress : d.Name,
                group = d.Type ?? "Unknown",
                val = size,
                status = d.IsDeleted ? "offline" : d.Status,
                ip = d.IpAddress,
                attachmentKind = d.AttachmentKind ?? "Unknown",
                attachmentPort = d.AttachmentPort,
                isInfrastructure = isInfra,
                childCount = childCountByParent.GetValueOrDefault(d.Id),
                securityGrade = d.SecurityGrade.ToString(),
                firstDiscoveredUtc = d.FirstDiscoveredUtc,
                vendor = d.Vendor,
            });

            var resolvedParentId = resolvedParentByChild.GetValueOrDefault(d.Id);
            var (sourceId, linkKind) = ResolveAttachmentLink(d, resolvedParentId, gatewayDevice?.Id, deviceById, rootId);
            links.Add(new
            {
                source = sourceId,
                target = d.Id.ToString(),
                kind = linkKind,
                port = d.AttachmentPort,
            });
        }

        // Host appliance when not already in inventory (local Node map only).
        if (!scopedToSite
            && !devices.Any(d => d.IpAddress == activeInterface.IpAddress)
            && activeInterface.IpAddress != gatewayIp
            && activeInterface.IpAddress != "0.0.0.0")
        {
            nodes.Add(new
            {
                id = "host_machine",
                name = Environment.MachineName,
                group = "Scanner",
                val = 12,
                status = "online",
                ip = activeInterface.IpAddress,
                attachmentKind = (string?)null,
                attachmentPort = (string?)null,
                isInfrastructure = false,
                childCount = 0,
                securityGrade = "Green",
                firstDiscoveredUtc = (DateTime?)null,
                vendor = (string?)null,
            });
            links.Add(new
            {
                source = rootId,
                target = "host_machine",
                kind = "gateway",
                port = (string?)null,
            });
        }

        var viewerNodeId = TopologyViewerResolver.ResolveViewerNodeId(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            activeInterface.IpAddress,
            devices,
            includeHostMachineNode: !scopedToSite);

        return Ok(new { nodes, links, viewerNodeId });
    }

    /// <summary>
    /// Resolve uplink parent by inventory GUID, then MAC (Hub/Node GUIDs diverge).
    /// </summary>
    private static Guid? ResolveAttachmentParentId(
        StacksAtlas.Core.Models.Device device,
        Guid? gatewayDeviceId,
        IReadOnlyDictionary<Guid, StacksAtlas.Core.Models.Device> deviceById)
    {
        if (device.AttachmentParentDeviceId is Guid parentId
            && deviceById.TryGetValue(parentId, out var byId)
            && !byId.IsDeleted
            && !byId.IsPermanentlyRemoved)
        {
            if (string.IsNullOrEmpty(device.NodeId)
                || string.IsNullOrEmpty(byId.NodeId)
                || string.Equals(device.NodeId, byId.NodeId, StringComparison.OrdinalIgnoreCase))
            {
                return parentId;
            }
        }

        var parentMac = DeviceMacNormalizer.Normalize(device.AttachmentParentMac);
        if (!string.IsNullOrEmpty(parentMac))
        {
            var byMac = deviceById.Values.FirstOrDefault(d =>
                !d.IsDeleted
                && !d.IsPermanentlyRemoved
                && d.Id != device.Id
                && DeviceMacNormalizer.Normalize(d.MacAddress) == parentMac
                && (string.IsNullOrEmpty(device.NodeId)
                    || string.IsNullOrEmpty(d.NodeId)
                    || string.Equals(device.NodeId, d.NodeId, StringComparison.OrdinalIgnoreCase)));
            if (byMac != null)
                return byMac.Id;
        }

        var parentIp = string.IsNullOrWhiteSpace(device.AttachmentParentIp)
            ? null
            : device.AttachmentParentIp.Trim();
        if (string.IsNullOrEmpty(parentIp))
            return null;

        var byIp = deviceById.Values.FirstOrDefault(d =>
            !d.IsDeleted
            && !d.IsPermanentlyRemoved
            && d.Id != device.Id
            && string.Equals(d.IpAddress, parentIp, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrEmpty(device.NodeId)
                || string.IsNullOrEmpty(d.NodeId)
                || string.Equals(device.NodeId, d.NodeId, StringComparison.OrdinalIgnoreCase)));

        return byIp?.Id;
    }

    /// <summary>
    /// Parent inventory link when valid and acyclic; otherwise gateway star (nothing disappears).
    /// </summary>
    private static (string SourceId, string Kind) ResolveAttachmentLink(
        StacksAtlas.Core.Models.Device device,
        Guid? resolvedParentId,
        Guid? gatewayDeviceId,
        IReadOnlyDictionary<Guid, StacksAtlas.Core.Models.Device> deviceById,
        string rootId)
    {
        if (resolvedParentId is not Guid parentId)
            return (rootId, "gateway");

        // Parent is the gateway inventory row: hang under root.
        if (gatewayDeviceId.HasValue && parentId == gatewayDeviceId.Value)
            return (rootId, NormalizeLinkKind(device.AttachmentKind));

        if (!deviceById.ContainsKey(parentId))
            return (rootId, "gateway");

        if (WouldCreateAttachmentCycle(device.Id, parentId, deviceById))
            return (rootId, "gateway");

        return (parentId.ToString(), NormalizeLinkKind(device.AttachmentKind));
    }

    private static bool WouldCreateAttachmentCycle(
        Guid deviceId,
        Guid parentId,
        IReadOnlyDictionary<Guid, StacksAtlas.Core.Models.Device> deviceById)
    {
        var guard = 0;
        var current = parentId;
        while (guard++ < 64)
        {
            if (current == deviceId)
                return true;
            if (!deviceById.TryGetValue(current, out var parent)
                || parent.AttachmentParentDeviceId is not Guid next)
            {
                return false;
            }
            current = next;
        }
        return true;
    }

    private static string NormalizeLinkKind(string? kind)
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
            _ => "Unknown",
        };
    }

    private static bool IsInfrastructureType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;
        var t = type.Trim();
        if (t.StartsWith("Network", StringComparison.OrdinalIgnoreCase))
            return true;
        return t.Equals("Switch", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Router", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Gateway", StringComparison.OrdinalIgnoreCase)
            || t.Equals("AP", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Firewall", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveNodeSize(string? type, bool isInfrastructure)
    {
        if (isInfrastructure)
            return 15;
        if (string.IsNullOrWhiteSpace(type))
            return 8;
        var t = type.Trim();
        if (t.StartsWith("Control", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Collaboration", StringComparison.OrdinalIgnoreCase))
            return 12;
        if (t.Equals("Server", StringComparison.OrdinalIgnoreCase)
            || t.Contains("NAS", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Storage NAS", StringComparison.OrdinalIgnoreCase))
            return 12;
        return 8;
    }
}

public record SetInterfaceRequest(string? Id);
