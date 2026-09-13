using LiteDB;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class ScanController(
    IDeepScanService deepScanService,
    IDeviceRepository deviceRepo,
    ILiteDatabase db,
    IServiceProvider serviceProvider,
    ILogger<ScanController> logger) : ControllerBase
{
    private readonly IDeepScanService _deepScanService = deepScanService;
    private readonly IDeviceRepository _deviceRepo = deviceRepo;
    private readonly ILiteDatabase _db = db;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<ScanController> _logger = logger;

    [HttpPost("deep/{deviceId}")]
    public IActionResult TriggerDeepScan(Guid deviceId)
    {
        var device = _deviceRepo.GetById(deviceId);

        if (device == null)
        {
            return NotFound("Device not found");
        }

        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            return BadRequest("Device has no IP address");
        }

        if (!_deepScanService.IsNmapAvailable())
        {
            return BadRequest(new { message = "Deep Scan requires Npcap/Nmap drivers which are not detected on this system." });
        }

        _logger.LogInformation("Triggering Deep Scan for device {DeviceId} ({IpAddress})", deviceId, device.IpAddress);
        _deepScanService.QueueScan(deviceId);

        return Ok(new { message = "Scan queued" });
    }

    [HttpGet("deep/{deviceId}/status")]
    public IActionResult GetDeepScanStatus(Guid deviceId)
    {
        if (_deviceRepo.GetById(deviceId) == null)
        {
            return NotFound("Device not found");
        }

        var (status, progress) = _deepScanService.GetScanStatus(deviceId);
        return Ok(new { status, progress });
        // Status: "idle", "running", "error: ..."
    }

    [HttpGet("deep/{deviceId}")]
    public IActionResult GetDeepScanResults(Guid deviceId)
    {
        if (_deviceRepo.GetById(deviceId) == null)
        {
            return NotFound("Device not found");
        }

        if (ExecutionState.IsHubBrainEnabled)
        {
            var remote = _serviceProvider.GetRequiredService<RemoteExecutionService>();
            return Ok(remote.GetScanResults(deviceId));
        }

        var services = _db.GetCollection<ServiceDetail>("service_details");
        var results = services.Find(x => x.DeviceId == deviceId).OrderBy(x => x.Port).ToList();
        return Ok(results);
    }
}
