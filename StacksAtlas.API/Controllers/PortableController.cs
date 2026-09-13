using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Controllers;

[ApiController]
[Route("api/system/portable")]
public class PortableController(ILogger<PortableController> logger) : ControllerBase
{
    private readonly ILogger<PortableController> _logger = logger;

    /// <summary>Stops portable mode, deletes local data after exit, and shuts down the API.</summary>
    [Authorize]
    [HttpPost("close")]
    public IActionResult Close()
    {
        if (!ExecutionState.IsPortable)
            return NotFound(new { message = "Close portable is only available in portable mode." });

        var cleanupRoot = PortableDataCleanup.ResolveCleanupDirectory();
        _logger.LogInformation("Portable close requested  -  cleanup target: {Directory}", cleanupRoot);

        PortableDataCleanup.ScheduleRemovalAndExit(_logger);

        return Ok(new
        {
            message = "Portable mode ended. Local data will be removed and StacksAtlas is shutting down.",
            dataDirectory = cleanupRoot
        });
    }
}
