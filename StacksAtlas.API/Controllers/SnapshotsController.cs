using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StacksAtlas.Core.Services.Governance;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Audit;

namespace StacksAtlas.API.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class SnapshotsController(ISnapshotService snapshotService, IAuditService audit, string dbPath) : ControllerBase
{
    private readonly ISnapshotService _snapshotService = snapshotService;
    private readonly IAuditService _audit = audit;
    private readonly string _dbPath = dbPath;

    [HttpGet]
    public IActionResult GetSnapshots()
    {
        return Ok(new
        {
            snapshots = _snapshotService.GetAllSnapshots(),
            hubDatabasePresent = _snapshotService.HubDatabasePresent
        });
    }

    [HttpPost]
    public IActionResult CreateSnapshot([FromBody] CreateSnapshotRequest request)
    {
        var label = request.Label ?? "manual";
        var result = _snapshotService.CreateSnapshot(label);
        _audit.Record(AuditActions.SnapshotCreate, "snapshot", result.ApplianceFileName, AuditOutcomes.Success, detail: $"label={label}");
        return Ok(new
        {
            fileName = result.ApplianceFileName,
            applianceFileName = result.ApplianceFileName,
            fleetFileName = result.FleetFileName,
            hubDatabasePresent = _snapshotService.HubDatabasePresent
        });
    }

    [HttpDelete("{*fileName}")]
    public IActionResult DeleteSnapshot(string fileName)
    {
        _snapshotService.DeleteSnapshot(fileName);
        _audit.Record(AuditActions.SnapshotDelete, "snapshot", fileName, AuditOutcomes.Success);
        return NoContent();
    }

    [HttpPost("restore/{*fileName}")]
    public IActionResult RestoreSnapshot(string fileName)
    {
        try
        {
            _snapshotService.StageRestore(fileName);
            var isFleet = FleetDatabaseGovernance.IsHubSnapshotFileName(fileName);
            var message = isFleet
                ? "Hub fleet snapshot staged for restore. Please restart the application to apply."
                : "Appliance snapshot staged for restore. Please restart the application to apply.";
            _audit.Record(AuditActions.SnapshotRestore, "snapshot", fileName, AuditOutcomes.Success, detail: isFleet ? "store=fleet" : "store=appliance");
            return Ok(new { message, store = isFleet ? "fleet" : "appliance" });
        }
        catch (FileNotFoundException)
        {
            return NotFound("Snapshot file not found.");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("fresh-start")]
    public IActionResult FreshStart([FromBody] FreshStartRequest? request)
    {
        try
        {
            var scope = NormalizeScope(ResolveScope(request));
            _snapshotService.FreshStart(scope);
            _audit.Record(AuditActions.SnapshotFreshStart, "snapshot", scope.ToString(), AuditOutcomes.Success);
            return Ok(new
            {
                message = DescribeFreshStart(scope, _snapshotService.HubDatabasePresent),
                scope = scope.ToString(),
                hubDatabasePresent = _snapshotService.HubDatabasePresent,
                isHubBrain = IsHubBrain()
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Fresh Start failed: {ex.Message}" });
        }
    }

    [HttpGet("download/{*fileName}")]
    public IActionResult DownloadSnapshot(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName != fileName)
            return BadRequest("Invalid filename.");

        var snapshotDir = Path.Combine(Path.GetDirectoryName(_dbPath)!, "Snapshots");
        var filePath = Path.Combine(snapshotDir, safeName);

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var bytes = System.IO.File.ReadAllBytes(filePath);
        return File(bytes, "application/octet-stream", safeName);
    }

    private FreshStartScope ResolveScope(FreshStartRequest? request)
    {
        if (request?.Scope != null)
            return request.Scope.Value;

        var hubPresent = _snapshotService.HubDatabasePresent;
        if (request?.WipeFleetDatabase == false)
            return FreshStartScope.ApplianceOnly;

        if (request?.WipeFleetDatabase == true)
            return FreshStartScope.ApplianceAndFleet;

        return hubPresent ? FreshStartScope.ApplianceAndFleet : FreshStartScope.ApplianceOnly;
    }

    private bool IsHubBrain() =>
        ExecutionState.IsHub && _snapshotService.HubDatabasePresent;

    /// <summary>
    /// Fleet-scoped operations are Hub-only. Nodes may reset local appliance data only.
    /// </summary>
    private FreshStartScope NormalizeScope(FreshStartScope scope)
    {
        if (IsHubBrain())
            return scope;

        return scope switch
        {
            FreshStartScope.ApplianceOnly => FreshStartScope.ApplianceOnly,
            FreshStartScope.FactoryReset => FreshStartScope.FactoryReset,
            FreshStartScope.FleetInventoryOnly =>
                throw new InvalidOperationException(
                    "Fleet inventory reset is only available on a Hub appliance with a fleet database."),
            FreshStartScope.ApplianceAndFleet => FreshStartScope.ApplianceOnly,
            _ => FreshStartScope.ApplianceOnly
        };
    }

    private static string DescribeFreshStart(FreshStartScope scope, bool hubDatabasePresent) => scope switch
    {
        FreshStartScope.ApplianceOnly when hubDatabasePresent =>
            "Fresh start staged. Appliance database (StacksAtlas.db) will be wiped on next startup. Fleet inventory preserved.",
        FreshStartScope.ApplianceOnly =>
            "Fresh start staged. Local site database (StacksAtlas.db) will be wiped on next startup. Re-enrollment with the Hub is required after restart.",
        FreshStartScope.FleetInventoryOnly =>
            "Fresh start staged. Hub fleet inventory (StacksAtlas.Hub.db) will be wiped on next startup. Appliance data preserved.",
        FreshStartScope.ApplianceAndFleet =>
            "Fresh start staged. Appliance and fleet databases will be wiped on next startup.",
        FreshStartScope.FactoryReset when hubDatabasePresent =>
            "Factory reset staged. Appliance and fleet databases plus JSON settings will be reset on next startup. License preserved.",
        FreshStartScope.FactoryReset =>
            "Factory reset staged. Local database and JSON settings will be reset on next startup. License preserved. Re-enroll with the Hub after restart.",
        _ => "Fresh start staged. Restart required."
    };
}

public record CreateSnapshotRequest(string? Label);

public record FreshStartRequest(FreshStartScope? Scope, bool? WipeFleetDatabase);
