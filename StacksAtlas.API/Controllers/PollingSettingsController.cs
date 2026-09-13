using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize]
[ApiController]
[Route("api/settings/polling")]

public class PollingSettingsController(
    PollingSettingsStore store,
    IAuditService audit) : ControllerBase
{
    private readonly PollingSettingsStore _store = store;
    private readonly IAuditService _audit = audit;

    [HttpGet]
    public ActionResult<PollingOptions> Get()
    {
        // Return valid persisted settings or defaults from store
        return Ok(_store.Current);
    }

    [HttpPut]
    public IActionResult Update([FromBody] PollingOptions updated)
    {
        // 1. Get current state (to preserve any fields not sent)
        var current = _store.Current;

        // 2. Update specific fields (Syncing Backend Loop with UI Loop)
        // usage: The UI slider controls "RefreshIntervalSeconds", but user expects "Engine Polling" to change.
        current.RefreshIntervalSeconds = updated.RefreshIntervalSeconds;
        
        // SYNC: Set backend loop to match the requested UI interval
        current.IntervalSeconds = updated.RefreshIntervalSeconds;

        // 3. Save merged state
        _store.Save(current);

        _audit.Record(
            AuditActions.SettingsPollingUpdate,
            "settings",
            "polling",
            AuditOutcomes.Success,
            detail: $"intervalSeconds={current.IntervalSeconds}");
        
        return Ok();
    }
}
