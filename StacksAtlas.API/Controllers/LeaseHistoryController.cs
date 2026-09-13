using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StacksAtlas.API.Services;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/leases")]
public class LeaseHistoryController(
    LeaseHistoryRepository repo,
    IDeviceRepository deviceRepo,
    RemoteExecutionService? remoteExecution = null) : ControllerBase
{
    private readonly LeaseHistoryRepository _repo = repo;
    private readonly IDeviceRepository _deviceRepo = deviceRepo;
    private readonly RemoteExecutionService? _remoteExecution = remoteExecution;

    [HttpGet("mac/{mac}")]
    public IActionResult GetByMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return BadRequest("MAC address required");

        var cleanMac = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();

        if (ExecutionState.IsHubBrainEnabled && _remoteExecution != null)
        {
            var device = _deviceRepo.GetByMac(cleanMac);
            if (device != null && !string.IsNullOrEmpty(device.NodeId))
            {
                return Ok(_remoteExecution.GetLeaseHistoryByMac(cleanMac));
            }
        }

        return Ok(_repo.GetHistoryForMac(cleanMac));
    }

    [HttpGet("ip/{ip}")]
    public IActionResult GetByIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return BadRequest("IP address required");

        if (ExecutionState.IsHubBrainEnabled && _remoteExecution != null)
        {
            var device = _deviceRepo.GetByIp(ip);
            if (device != null && !string.IsNullOrEmpty(device.NodeId))
            {
                return Ok(_remoteExecution.GetLeaseHistoryByIp(ip));
            }
        }

        return Ok(_repo.GetHistoryForIp(ip));
    }
}
