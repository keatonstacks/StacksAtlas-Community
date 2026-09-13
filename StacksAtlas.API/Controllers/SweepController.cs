using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/sweep")]
// IDE0290: Primary Constructor applied
public class SweepController(
    SweepHistoryRepository history,
    SystemStateProvider stateProvider) : ControllerBase
{
    private readonly SweepHistoryRepository _history = history;
    private readonly SystemStateProvider _stateProvider = stateProvider;

    // NEW: GET /api/sweep/status
    // Used by the UI Header for the "Online" dot and "Scanning" spinner
    [HttpGet("status")]
    public ActionResult<StacksAtlas.Core.Models.SystemStatus> GetStatus()
    {
        var status = _stateProvider.GetCurrentStatus();
        return Ok(status);
    }

    // GET: /api/sweep/latest
    [HttpGet("latest")]
    public ActionResult<SweepHistoryEntry?> GetLatest()
    {
        var latest = _history.GetLatest();
        return Ok(latest);
    }

    // GET: /api/sweep/recent?limit=50
    [HttpGet("recent")]
    public ActionResult<IEnumerable<SweepHistoryEntry>> GetRecent([FromQuery] int limit = 50)
    {
        // Modern C# way to handle range validation
        limit = Math.Clamp(limit, 1, 500);

        var items = _history.GetRecent(limit);
        return Ok(items);
    }
}
