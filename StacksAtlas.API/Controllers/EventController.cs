#pragma warning disable CA1873

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/events")]
public class EventController(IEventRepository repo, ILogger<EventController> logger) : ControllerBase
{
    private readonly IEventRepository _repo = repo;
    private readonly ILogger<EventController> _logger = logger;

    [HttpGet("recent")]
    public ActionResult<IEnumerable<SystemEvent>> GetRecent([FromQuery] int limit = 200, [FromQuery(Name = "nodeId")] string[]? nodeIds = null) // Increased default for marriage logic
    {
        return Ok(_repo.GetRecent(limit, nodeIds));
    }

    /// <summary>
    /// Manually inject a system event (Used by Report Service for Security Hygiene Alerts)
    /// </summary>
    [HttpPost]
    public ActionResult AddEvent([FromBody] SystemEvent newEvent)
    {
        if (newEvent == null) return BadRequest();

        // The method name in your repo is AddEvent, not Add
        _repo.AddEvent(newEvent);

        return Ok(new { Success = true });
    }

    [HttpDelete]
    public ActionResult ClearAll([FromQuery(Name = "nodeId")] string[]? nodeIds = null)
    {
        var removed = _repo.ClearAll(nodeIds);
        return Ok(new { Removed = removed });
    }

    [HttpDelete("{id}")]
    public ActionResult DeleteById(string id)
    {
        string decodedId = System.Net.WebUtility.UrlDecode(id);

        if (string.IsNullOrWhiteSpace(decodedId) || decodedId.Contains("[object Object]", StringComparison.Ordinal))
        {
            _logger.LogWarning("Invalid ID format blocked: {Id}", decodedId);
            return BadRequest(new { Message = "Malformed ID. Check frontend serialization." });
        }

        _logger.LogInformation("Processing delete request for ID: {Id}", decodedId);

        var removed = _repo.DeleteById(decodedId);

        if (!removed)
        {
            _logger.LogWarning("Delete failed: No event matches ID {Id} in database", decodedId);
            return NotFound(new { Message = $"Event {decodedId} not found" });
        }

        return Ok(new { Removed = true });
    }
}
