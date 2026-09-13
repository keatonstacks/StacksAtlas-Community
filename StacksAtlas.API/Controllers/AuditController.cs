using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Abstractions;

namespace StacksAtlas.API.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/audit")]
public class AuditController(IAuditEventRepository repository) : ControllerBase
{
    private readonly IAuditEventRepository _repository = repository;

    /// <summary>
    /// Paginated admin audit trail (append-only human actions).
    /// </summary>
    [HttpGet("events")]
    public IActionResult GetEvents(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        [FromQuery] string? action = null,
        [FromQuery(Name = "nodeId")] string[]? nodeIds = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] DateTime? until = null,
        [FromQuery] bool hubOnly = false)
    {
        DateTime? sinceUtc = since.HasValue
            ? DateTime.SpecifyKind(since.Value.ToUniversalTime(), DateTimeKind.Utc)
            : null;
        DateTime? untilUtc = until.HasValue
            ? DateTime.SpecifyKind(until.Value.ToUniversalTime(), DateTimeKind.Utc)
            : null;

        var events = _repository.Query(skip, take, action, nodeIds, sinceUtc, untilUtc, hubOnly);
        return Ok(new
        {
            items = events,
            skip,
            take = Math.Clamp(take, 1, 500),
            count = events.Count,
            hasMore = events.Count >= Math.Clamp(take, 1, 500),
        });
    }
}
