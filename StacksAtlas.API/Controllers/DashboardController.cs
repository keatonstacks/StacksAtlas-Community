using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Data;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DashboardController(IDeviceRepository repo) : ControllerBase
{
    private readonly IDeviceRepository _repo = repo;

    /// <summary>
    /// Returns aggregated dashboard metrics without fetching full device list.
    /// This is a lightweight endpoint optimized for Dashboard page load.
    /// </summary>
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        var devices = _repo.GetSummaryData();
        
        var totalDevices = devices.Count;
        var onlineCount = devices.Count(d => d.Status?.ToLower() == "online");
        var offlineCount = totalDevices - onlineCount;
        
        // Vendor breakdown (top 10 + "Other")
        var vendorGroups = devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Vendor))
            .GroupBy(d => d.Vendor)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new { vendor = g.Key, count = g.Count() })
            .ToList();
        
        // Type breakdown
        var typeGroups = devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Type))
            .GroupBy(d => d.Type)
            .Select(g => new { type = g.Key, count = g.Count() })
            .ToList();

        return Ok(new
        {
            totalDevices,
            onlineCount,
            offlineCount,
            vendors = vendorGroups,
            types = typeGroups,
            isHub = StacksAtlas.Core.State.ExecutionState.IsHub,
            timestamp = DateTime.UtcNow
        });
    }
}
